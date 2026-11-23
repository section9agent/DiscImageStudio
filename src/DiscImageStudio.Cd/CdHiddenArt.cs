using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DiscImageStudio.Core;

namespace DiscImageStudio.Cd;

/// <summary>Where the picture goes on an audio CD that also carries music.</summary>
public enum CdArtPlacement
{
    /// <summary>The original behaviour: the picture is one audio track covering the disc.</summary>
    WholeDisc,

    /// <summary>The picture sits in track 1's pre-gap (index 00) at the inner ring and the music
    /// follows. Players start at index 01, so they skip the picture unless rewound into it.</summary>
    HiddenPregap,

    /// <summary>Music is the first session; the picture is a second session in the outer ring.
    /// Standard CD players only read the first session's table of contents.</summary>
    SecondSession,
}

/// <summary>One music file for a hidden-art disc: 44.1 kHz, 16-bit stereo PCM WAV.</summary>
public sealed record CdMusicTrack(string Path, long AudioOffset, long AudioBytes)
{
    /// <summary>Burners pad the last partial sector of a track with silence.</summary>
    public long Sectors => (AudioBytes + CdDiscParameters.BytesPerSector - 1) / CdDiscParameters.BytesPerSector;

    public TimeSpan Duration => TimeSpan.FromSeconds(Sectors / 75.0);

    public static CdMusicTrack Load(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        using FileStream input = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        (long offset, long length) = CdWaveFile.ReadAudioRange(input);
        return new CdMusicTrack(fullPath, offset, length);
    }
}

/// <summary>The sector layout of a hidden-art disc. Sector numbers are logical block addresses
/// where 0 is the first sector after the standard 2-second lead-in pre-gap; this is also byte 0
/// of the generation geometry, so existing calibration presets keep their meaning.</summary>
public sealed record CdHiddenArtPlan(
    CdArtPlacement Placement,
    long ArtStartSector,
    long ArtEndSector,
    IReadOnlyList<CdMusicTrack> Music,
    long MusicGapSectors,
    bool SessionStartMeasured)
{
    /// <summary>Red Book minimum track length (4 seconds); IMAPI enforces it too.</summary>
    public const long MinimumTrackSectors = 300;

    /// <summary>Silent pre-gap every disc has before LBA 0.</summary>
    public const long StandardPregapSectors = 150;

    /// <summary>Lead-out written after a first session (90 s).</summary>
    public const long FirstSessionLeadOutSectors = 6750;

    /// <summary>Lead-in written before a later session (60 s).</summary>
    public const long NextSessionLeadInSectors = 4500;

    /// <summary>Estimated gap between the end of session 1 and the first user sector of session 2:
    /// lead-out + lead-in + the new track's pre-gap. The drive decides the real value; read it
    /// with <c>cdrecord -msinfo</c> after burning session 1.</summary>
    public const long SessionGapSectors = FirstSessionLeadOutSectors + NextSessionLeadInSectors + StandardPregapSectors;

    public long ArtSectors => ArtEndSector - ArtStartSector;

    /// <summary>First sector of the first song.</summary>
    public long MusicStartSector => Placement == CdArtPlacement.HiddenPregap ? ArtEndSector : 0;

    /// <summary>Sector after the last song (where session 1's lead-out starts).</summary>
    public long MusicEndSector => MusicStartSector + MusicSectors;

    public long MusicSectors => Music.Sum(track => track.Sectors) + Math.Max(0, Music.Count - 1) * MusicGapSectors;

    /// <summary>Last sector the layout uses.</summary>
    public long EndSector => Placement == CdArtPlacement.SecondSession ? ArtEndSector : MusicEndSector;

    /// <summary>Pre-gap file length for <see cref="CdArtPlacement.HiddenPregap"/>: the standard
    /// 150 silent sectors followed by the picture, so the picture lands where it always did.</summary>
    public long PregapFileSectors => StandardPregapSectors + ArtSectors;

    public static CdHiddenArtPlan ForPregap(CdDiscParameters parameters, double artOuterRadiusMm,
        IReadOnlyList<CdMusicTrack> music, long musicGapSectors = StandardPregapSectors)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        long artEnd = CdHiddenArt.SectorAtRadius(parameters, artOuterRadiusMm);
        CdHiddenArtPlan plan = new(CdArtPlacement.HiddenPregap, 0, artEnd, music, musicGapSectors, false);
        plan.Validate(parameters);
        return plan;
    }

    /// <summary>Session-2 layout. Without <paramref name="measuredSessionStart"/> the start is
    /// estimated from the music length; the art then fills the disc out to the geometry's output
    /// sectors (or <paramref name="artOuterRadiusMm"/> if given).</summary>
    public static CdHiddenArtPlan ForSecondSession(CdDiscParameters parameters,
        IReadOnlyList<CdMusicTrack> music, long musicGapSectors = StandardPregapSectors,
        long? measuredSessionStart = null, double? artOuterRadiusMm = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        long musicEnd = music.Sum(track => track.Sectors) + Math.Max(0, music.Count - 1) * musicGapSectors;
        long start = measuredSessionStart ?? musicEnd + SessionGapSectors;
        long end = artOuterRadiusMm is { } radius
            ? CdHiddenArt.SectorAtRadius(parameters, radius)
            : parameters.OutputSectors;
        CdHiddenArtPlan plan = new(CdArtPlacement.SecondSession, start, end, music, musicGapSectors,
            measuredSessionStart.HasValue);
        plan.Validate(parameters);
        return plan;
    }

    public void Validate(CdDiscParameters parameters)
    {
        if (Placement == CdArtPlacement.WholeDisc)
            throw new ArgumentException("Whole-disc art has no hidden-art layout.");
        if (MusicGapSectors is not (0 or StandardPregapSectors))
            throw new ArgumentException("The gap between songs must be 0 (gapless) or 150 sectors (2 seconds).");
        foreach (CdMusicTrack track in Music)
        {
            if (track.Sectors < MinimumTrackSectors)
                throw new ArgumentException($"'{System.IO.Path.GetFileName(track.Path)}' is shorter than 4 seconds; CD tracks must be at least 4 seconds long.");
        }

        if (Music.Count == 0)
            throw new ArgumentException("Add at least one music WAV file.");
        if (Music.Count > 98)
            throw new ArgumentException("A CD holds at most 99 tracks (98 songs plus the art track).");
        if (ArtSectors <= 0)
            throw new ArgumentException(Placement == CdArtPlacement.HiddenPregap
                ? "The art ring is empty; increase the art outer radius."
                : $"The music fills the disc; there is no room left for a second session (session 2 would start at sector {ArtStartSector:N0}, the disc ends at {ArtEndSector:N0}).");
        if (Placement == CdArtPlacement.SecondSession && ArtSectors < MinimumTrackSectors)
            throw new ArgumentException("The second-session art ring is shorter than the 4-second minimum track length; shorten the music.");
        if (EndSector > parameters.OutputSectors)
            throw new ArgumentException(
                $"The layout needs {EndSector:N0} sectors but the disc geometry only has {parameters.OutputSectors:N0} "
                + $"({FormatTime(EndSector)} vs {FormatTime(parameters.OutputSectors)}). Shorten the music"
                + (Placement == CdArtPlacement.HiddenPregap ? " or shrink the art ring." : "."));
    }

    public static string FormatTime(long sectors)
    {
        long frames = Math.Max(0, sectors);
        return string.Create(CultureInfo.InvariantCulture, $"{frames / 4500:00}:{frames / 75 % 60:00}:{frames % 75:00}");
    }

    /// <summary>Plain-English summary for logs and the UI.</summary>
    public string Describe(CdDiscParameters parameters)
    {
        StringBuilder text = new();
        double artInner = parameters.RadiusFromByte(ArtStartSector * CdDiscParameters.BytesPerSector);
        double artOuter = parameters.RadiusFromByte(ArtEndSector * CdDiscParameters.BytesPerSector);
        text.Append(CultureInfo.InvariantCulture,
            $"Art ring {artInner:0.0}-{artOuter:0.0} mm, {ArtSectors:N0} sectors ({FormatTime(ArtSectors)})");
        text.Append(CultureInfo.InvariantCulture,
            $"; music {Music.Count} track(s), {FormatTime(MusicSectors)}");
        if (Placement == CdArtPlacement.SecondSession)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"; session 2 starts at sector {ArtStartSector:N0} ({(SessionStartMeasured ? "measured" : "estimated")})");
        }

        text.Append(CultureInfo.InvariantCulture,
            $"; uses {EndSector:N0} of {parameters.OutputSectors:N0} sectors.");
        return text.ToString();
    }
}

/// <summary>Generation, CUE sheets and burn scripts for hidden-art discs.</summary>
public static class CdHiddenArt
{
    /// <summary>First sector whose start lies at or beyond <paramref name="radiusMm"/>.</summary>
    public static long SectorAtRadius(CdDiscParameters parameters, double radiusMm)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!double.IsFinite(radiusMm) || radiusMm <= parameters.InnerRadiusMm)
            throw new ArgumentOutOfRangeException(nameof(radiusMm),
                $"The art radius must be larger than the inner radius ({parameters.InnerRadiusMm:0.0} mm).");
        long low = 0;
        long high = parameters.Sectors;
        while (low < high)
        {
            long middle = low + ((high - low) / 2);
            if (parameters.RadiusFromByte(middle * CdDiscParameters.BytesPerSector) >= radiusMm) high = middle;
            else low = middle + 1;
        }

        return low;
    }

    /// <summary>Writes the picture WAV for <paramref name="plan"/>: the track 1 pre-gap for
    /// <see cref="CdArtPlacement.HiddenPregap"/> or the session 2 track for
    /// <see cref="CdArtPlacement.SecondSession"/>. Also writes the CUE sheet (pre-gap) or the
    /// cdrecord script (session 2) and a plan sidecar next to it.</summary>
    public static CdHiddenArtResult Generate(string imagePath, string outputPath, CdDiscParameters parameters,
        CdHiddenArtPlan plan, ToneProfile tone, bool interleave = true,
        Action<CdProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(parameters);
        plan.Validate(parameters);
        string output = Path.GetFullPath(outputPath);
        if (!Path.GetExtension(output).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Hidden-art output must be a .wav file.", nameof(outputPath));
        string source = Path.GetFullPath(imagePath);
        if (source.Equals(output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The output must not overwrite the source image.", nameof(outputPath));

        long fileSectors = plan.Placement == CdArtPlacement.HiddenPregap ? plan.PregapFileSectors : plan.ArtSectors;
        long fileBytes = checked(fileSectors * CdDiscParameters.BytesPerSector);
        CdWaveFile.ValidateLength(fileBytes);
        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Generation always starts at geometry byte 0; for session 2 the sectors in front of the
        // art (the music and the session gap) are generated and dropped, so the interleave and
        // dither state at the art's first sector match a full-disc track exactly.
        CdDiscParameters artParameters = parameters with { UsableSectors = plan.ArtEndSector };
        CdGenerationSummary summary;
        using (FileStream file = new(output, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 1024 * 1024, FileOptions.SequentialScan))
        {
            CdWaveFile.WriteHeader(file, fileBytes);
            if (plan.Placement == CdArtPlacement.HiddenPregap)
            {
                WriteSilence(file, CdHiddenArtPlan.StandardPregapSectors);
                summary = CdTrackGenerator.GenerateToStream(source, file, artParameters, interleave, progress,
                    cancellationToken, output, CdAudioByteOrder.LittleEndian, tone);
            }
            else
            {
                using SkipStream skip = new(file, checked(plan.ArtStartSector * CdDiscParameters.BytesPerSector));
                summary = CdTrackGenerator.GenerateToStream(source, skip, artParameters, interleave, progress,
                    cancellationToken, output, CdAudioByteOrder.LittleEndian, tone);
                if (skip.Forwarded != fileBytes)
                    throw new InvalidOperationException($"Session 2 art wrote {skip.Forwarded} bytes, expected {fileBytes}.");
            }

            file.Flush();
            if (file.Length != fileBytes + CdWaveFile.HeaderBytes)
                throw new InvalidOperationException($"Art WAV is {file.Length} bytes, expected {fileBytes + CdWaveFile.HeaderBytes}.");
        }

        string planPath = output + ".hidden-art.json";
        File.WriteAllText(planPath, JsonSerializer.Serialize(new
        {
            format = "hidden-art-plan-v1",
            placement = plan.Placement.ToString(),
            plan.ArtStartSector,
            plan.ArtEndSector,
            plan.MusicGapSectors,
            plan.SessionStartMeasured,
            music = plan.Music.Select(track => new { track.Path, track.Sectors }),
            sourceImage = source,
            interleave,
        }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        string? cue = null;
        string? script = null;
        if (plan.Placement == CdArtPlacement.HiddenPregap)
        {
            cue = Path.ChangeExtension(output, ".cue");
            File.WriteAllText(cue, BuildPregapCue(output, plan), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        else
        {
            script = Path.ChangeExtension(output, ".cdrecord.cmd");
            File.WriteAllText(script, CdRecord.BuildSessionScript(output, plan), new UTF8Encoding(false));
        }

        return new CdHiddenArtResult(output, cue, script, planPath, summary.Elapsed, plan.Describe(parameters));
    }

    /// <summary>Where a hidden-art art track sits on the disc, from the plan sidecar written
    /// next to it: the disc byte of the file's first sample (negative for a pre-gap file, which
    /// starts with the 150 silent lead-in sectors) and the art's sector range. False for any
    /// other track.</summary>
    public static bool TryReadTrackPlacement(string trackPath, out long discStartByte,
        out long artStartSector, out long artEndSector)
    {
        discStartByte = 0;
        artStartSector = 0;
        artEndSector = 0;
        string sidecar = Path.GetFullPath(trackPath) + ".hidden-art.json";
        if (!File.Exists(sidecar)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(sidecar));
            JsonElement root = document.RootElement;
            if (root.GetProperty("format").GetString() != "hidden-art-plan-v1") return false;
            artStartSector = root.GetProperty(nameof(CdHiddenArtPlan.ArtStartSector)).GetInt64();
            artEndSector = root.GetProperty(nameof(CdHiddenArtPlan.ArtEndSector)).GetInt64();
            discStartByte = root.GetProperty("placement").GetString() == nameof(CdArtPlacement.HiddenPregap)
                ? -CdHiddenArtPlan.StandardPregapSectors * CdDiscParameters.BytesPerSector
                : artStartSector * CdDiscParameters.BytesPerSector;
            return artEndSector > artStartSector;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
                                              or InvalidOperationException or IOException or FormatException)
        {
            return false;
        }
    }

    /// <summary>CUE sheet for ImgBurn/cdrdao-style DAO burners: the picture is INDEX 00 of
    /// track 1, song 1 starts at INDEX 01.</summary>
    public static string BuildPregapCue(string artWavePath, CdHiddenArtPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Placement != CdArtPlacement.HiddenPregap)
            throw new ArgumentException("A pre-gap CUE sheet needs the hidden pre-gap layout.", nameof(plan));
        StringBuilder cue = new();
        cue.Append("REM Disc Image Studio hidden pre-gap art: rewinding before track 1 plays the art as noise.\r\n");
        cue.Append(CultureInfo.InvariantCulture, $"FILE \"{Path.GetFullPath(artWavePath)}\" WAVE\r\n");
        cue.Append("  TRACK 01 AUDIO\r\n");
        cue.Append("    INDEX 00 00:00:00\r\n");
        for (int index = 0; index < plan.Music.Count; index++)
        {
            cue.Append(CultureInfo.InvariantCulture, $"FILE \"{plan.Music[index].Path}\" WAVE\r\n");
            if (index > 0)
            {
                cue.Append(CultureInfo.InvariantCulture, $"  TRACK {index + 1:00} AUDIO\r\n");
                if (plan.MusicGapSectors > 0) cue.Append("    PREGAP 00:02:00\r\n");
            }

            cue.Append("    INDEX 01 00:00:00\r\n");
        }

        return cue.ToString();
    }

    private static void WriteSilence(Stream output, long sectors)
    {
        byte[] silence = new byte[CdDiscParameters.BytesPerSector * 75];
        long remaining = sectors * CdDiscParameters.BytesPerSector;
        while (remaining > 0)
        {
            int count = (int)Math.Min(silence.Length, remaining);
            output.Write(silence, 0, count);
            remaining -= count;
        }
    }

    /// <summary>Write-only stream that discards the first <c>skip</c> bytes.</summary>
    private sealed class SkipStream(Stream inner, long skip) : Stream
    {
        private long _remainingSkip = skip;

        internal long Forwarded { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_remainingSkip > 0)
            {
                int dropped = (int)Math.Min(_remainingSkip, buffer.Length);
                _remainingSkip -= dropped;
                buffer = buffer[dropped..];
            }

            if (buffer.IsEmpty) return;
            inner.Write(buffer);
            Forwarded += buffer.Length;
        }

        public override void WriteByte(byte value) => Write([value]);
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

public sealed record CdHiddenArtResult(
    string ArtWavePath,
    string? CueSheetPath,
    string? CdRecordScriptPath,
    string PlanPath,
    TimeSpan Elapsed,
    string Summary);
