using System.IO;
using DiscImageStudio.Cd;
using DiscImageStudio.Core;
using DiscImageStudio.ImageIO;

/// <summary>Hidden-art CD layouts: the art bytes must be exactly the ones a whole-disc track
/// would have at the same sectors, and the CUE / cdrecord plumbing must say what it means.</summary>
internal static class CdHiddenArtTests
{
    private const int WaveHeaderBytes = 44;

    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"disc-hidden-art-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string image = Path.Combine(directory, "art.png");
            WriteCheckerPng(image);
            string songA = WriteSong(directory, "a.wav", 400);
            string songB = WriteSong(directory, "b.wav", 350);
            CdMusicTrack[] music = [CdMusicTrack.Load(songA), CdMusicTrack.Load(songB)];
            Require(music[0].Sectors == 400 && music[1].Sectors == 350, "music lengths round up to whole sectors");

            CdDiscParameters parameters = new(24.5, 56.8, 359_849, UsableSectors: 359_847);
            TestRadiusLookup(parameters);
            TestPregap(directory, image, parameters, music);
            TestSecondSession(directory, image, parameters, music);
            TestValidation(parameters, music);
            TestCdRecordParsing();
            TestCdRecordArguments(music);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
    }

    private static void TestRadiusLookup(CdDiscParameters parameters)
    {
        long sector = CdHiddenArt.SectorAtRadius(parameters, 30.0);
        double at = parameters.RadiusFromByte(sector * CdDiscParameters.BytesPerSector);
        double before = parameters.RadiusFromByte((sector - 1) * CdDiscParameters.BytesPerSector);
        Require(at >= 30.0 && before < 30.0, "SectorAtRadius returns the first sector at the radius");
    }

    private static void TestPregap(string directory, string image, CdDiscParameters parameters, CdMusicTrack[] music)
    {
        CdHiddenArtPlan plan = CdHiddenArtPlan.ForPregap(parameters, 24.7, music);
        Require(plan.ArtStartSector == 0 && plan.MusicStartSector == plan.ArtEndSector,
            "pre-gap art starts at LBA 0 and song 1 starts right after it");
        Require(plan.MusicEndSector == plan.ArtEndSector + 400 + 150 + 350, "songs keep a 2-second gap");
        string output = Path.Combine(directory, "pregap", "art.wav");
        CdHiddenArtResult result = CdHiddenArt.Generate(image, output, parameters, plan, ToneProfile.LegacyBinary);
        byte[] art = File.ReadAllBytes(output)[WaveHeaderBytes..];
        byte[] reference = Reference(directory, image, parameters, plan.ArtEndSector);
        int silence = (int)(CdHiddenArtPlan.StandardPregapSectors * CdDiscParameters.BytesPerSector);
        Require(art.Length == silence + reference.Length, "pre-gap file is 150 silent sectors plus the art");
        Require(art.AsSpan(0, silence).IndexOfAnyExcept((byte)0) < 0, "pre-gap file starts with silence");
        Require(art.AsSpan(silence).SequenceEqual(reference), "pre-gap art matches a whole-disc track sector for sector");
        string cue = File.ReadAllText(result.CueSheetPath!);
        Require(cue.Contains("INDEX 00 00:00:00", StringComparison.Ordinal)
                && cue.IndexOf("INDEX 00", StringComparison.Ordinal) < cue.IndexOf("INDEX 01", StringComparison.Ordinal)
                && cue.Contains("TRACK 02 AUDIO", StringComparison.Ordinal)
                && cue.Contains("PREGAP 00:02:00", StringComparison.Ordinal),
            "pre-gap CUE puts the art in INDEX 00 and song 1 at INDEX 01");
    }

    private static void TestSecondSession(string directory, string image, CdDiscParameters parameters, CdMusicTrack[] music)
    {
        CdHiddenArtPlan estimated = CdHiddenArtPlan.ForSecondSession(parameters, music, artOuterRadiusMm: 30);
        Require(estimated.ArtStartSector == 900 + CdHiddenArtPlan.SessionGapSectors && !estimated.SessionStartMeasured,
            "session 2 start is estimated from the music plus lead-out, lead-in and pre-gap");
        CdHiddenArtPlan plan = CdHiddenArtPlan.ForSecondSession(parameters, music, measuredSessionStart: 12_345,
            artOuterRadiusMm: 27.5);
        Require(plan.ArtStartSector == 12_345 && plan.SessionStartMeasured, "a measured -msinfo start is used as is");
        string output = Path.Combine(directory, "session2", "art.wav");
        CdHiddenArtResult result = CdHiddenArt.Generate(image, output, parameters, plan, ToneProfile.LegacyBinary);
        byte[] art = File.ReadAllBytes(output)[WaveHeaderBytes..];
        byte[] reference = Reference(directory, image, parameters, plan.ArtEndSector);
        Require(art.AsSpan().SequenceEqual(reference.AsSpan((int)(plan.ArtStartSector * CdDiscParameters.BytesPerSector))),
            "session 2 art equals the same sectors of a whole-disc track");
        Require(result.CdRecordScriptPath is not null && File.ReadAllText(result.CdRecordScriptPath).Contains("-msinfo", StringComparison.Ordinal),
            "session 2 export writes a cdrecord script that checks -msinfo");
    }

    private static void TestValidation(CdDiscParameters parameters, CdMusicTrack[] music)
    {
        Throws(() => CdHiddenArtPlan.ForPregap(parameters, 30, []), "a hidden-art disc needs music");
        Throws(() => CdHiddenArtPlan.ForPregap(parameters, 30, music, musicGapSectors: 75), "only 0 or 2-second gaps");
        CdMusicTrack tooLong = music[0] with { AudioBytes = 400_000L * CdDiscParameters.BytesPerSector };
        Throws(() => CdHiddenArtPlan.ForSecondSession(parameters, [tooLong]), "music that fills the disc leaves no session 2");
        CdMusicTrack tooShort = music[0] with { AudioBytes = 10 * CdDiscParameters.BytesPerSector };
        Throws(() => CdHiddenArtPlan.ForPregap(parameters, 30, [tooShort]), "songs shorter than 4 seconds are rejected");
    }

    private static void TestCdRecordParsing()
    {
        const string scanbus = "scsibus1:\n\t1,0,0\t100) 'HL-DT-ST' 'DVDRAM GH24NSD1 ' 'LG00' Removable CD-ROM\r\n\t1,1,0\t101) *\n";
        IReadOnlyList<CdRecordDevice> devices = CdRecord.ParseScanbus(scanbus);
        Require(devices.Count == 1 && devices[0].Address == "1,0,0" && devices[0].Description == "HL-DT-ST DVDRAM GH24NSD1",
            "scanbus parsing keeps recorders and skips empty slots");
        Require(CdRecord.ParseMsinfo("cdrecord: some note\n0,13125\n") == (0, 13125), "msinfo parsing reads the next session start");
        Throws(() => CdRecord.ParseMsinfo("cdrecord: Cannot read session offset\n"), "msinfo without numbers is an error");
    }

    private static void TestCdRecordArguments(CdMusicTrack[] music)
    {
        CdHiddenArtPlan gapless = new(CdArtPlacement.SecondSession, 20_000, 30_000, music, 0, true);
        IReadOnlyList<string> first = CdRecord.SessionOneArguments("1,0,0", gapless, speed: 8);
        Require(first.Contains("-multi") && first.Contains("-sao") && first.Contains("speed=8") && first.Count(a => a == "pregap=0") == 1,
            "session 1 is disc-at-once, left open, gapless when asked");
        IReadOnlyList<string> second = CdRecord.SessionTwoArguments("1,0,0", "art.wav");
        Require(second.Contains("-tao") && !second.Contains("-multi"), "session 2 is track-at-once and closes the disc");
        Require(CdRecord.Join(["dev=1,0,0", @"C:\My Music\a.wav"]) == "dev=1,0,0 \"C:\\My Music\\a.wav\"", "arguments with spaces are quoted");
    }

    private static byte[] Reference(string directory, string image, CdDiscParameters parameters, long sectors)
    {
        string path = Path.Combine(directory, $"reference-{sectors}.wav");
        CdTrackGenerator.Generate(image, path, parameters with { UsableSectors = sectors }, interleave: true, writeCue: false);
        return File.ReadAllBytes(path)[WaveHeaderBytes..];
    }

    private static string WriteSong(string directory, string name, int sectors)
    {
        string path = Path.Combine(directory, name);
        long bytes = sectors * (long)CdDiscParameters.BytesPerSector - 100; // a partial last sector
        bytes -= bytes % 4;
        using FileStream file = File.Create(path);
        WriteWaveHeader(file, bytes);
        byte[] data = new byte[bytes];
        new Random(sectors).NextBytes(data);
        file.Write(data);
        return path;
    }

    private static void WriteWaveHeader(Stream output, long audioBytes)
    {
        using BinaryWriter writer = new(output, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write((uint)(audioBytes + 36));
        writer.Write("WAVEfmt "u8);
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        writer.Write(44100u);
        writer.Write(176400u);
        writer.Write((ushort)4);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write((uint)audioBytes);
    }

    private static void WriteCheckerPng(string path)
    {
        const int size = 128;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int offset = ((y * size) + x) * 4;
            byte value = ((x / 8) + (y / 8)) % 2 == 0 ? (byte)0 : (byte)255;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }

        RasterCodec.SavePng(path, size, size, pixels, grayscale: false);
    }

    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected an exception: " + name);
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("CD hidden-art test failed: " + name);
        Console.WriteLine("PASS " + name);
    }
}
