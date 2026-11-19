using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DiscImageStudio.Burning;

/// <summary>One audio file region to burn: PCM sample bytes inside a WAV file.</summary>
public sealed record CdAudioFileRegion(string Path, long Offset, long Length);

/// <summary>An audio CD whose track 1 pre-gap (index 00) holds a picture: the pre-gap stream
/// must already begin with the standard 150 silent sectors.</summary>
public sealed record HiddenPregapCdRequest(
    string DeviceId,
    CdAudioFileRegion Pregap,
    IReadOnlyList<CdAudioFileRegion> Tracks,
    bool Gapless,
    OpticalWriteSpeed? WriteSpeed = null);

/// <summary>Burns <see cref="HiddenPregapCdRequest"/> disc-at-once through IMAPI2's raw CD
/// writer, whose image creator supports exactly this: <c>AddSpecialPregap</c> before the first
/// <c>AddTrack</c>. The disc is closed afterwards.</summary>
public static class HiddenPregapCdBurner
{
    private const int AudioSectorType = 0;
    private const int SectorBytes = 2352;
    private const int StandardPregapSectors = 150;

    public static Task BurnAsync(HiddenPregapCdRequest request, IProgress<OpticalBurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Tracks.Count == 0) throw new ArgumentException("Add at least one music track.", nameof(request));
        return ImapiCom.RunOnStaThread(() =>
        {
            Burn(request, progress, cancellationToken);
            return true;
        });
    }

    private static void Burn(HiddenPregapCdRequest request, IProgress<OpticalBurnProgress>? progress,
        CancellationToken cancellationToken)
    {
        long pregapSectors = SectorsOf(request.Pregap);
        long artSectors = pregapSectors - StandardPregapSectors;
        long[] trackSectors = request.Tracks.Select(SectorsOf).ToArray();
        long totalBytes = (pregapSectors + trackSectors.Sum()) * SectorBytes;
        void Report(string stage, string message, long completed = 0)
            => progress?.Report(new OpticalBurnProgress(stage, message, completed, totalBytes));

        cancellationToken.ThrowIfCancellationRequested();
        Report("device", "Connecting to the recorder…");
        dynamic recorder = ImapiCom.CreateRecorder(request.DeviceId);
        dynamic? format = null;
        List<IDisposable> streams = [];
        bool prepared = false;
        try
        {
            format = ImapiCom.CreateComObject("IMAPI2.MsftDiscFormat2RawCD");
            format.Recorder = recorder;
            format.ClientName = "Disc Image Studio";
            if (!(bool)format.IsRecorderSupported(recorder))
                throw new InvalidOperationException("This recorder does not support raw disc-at-once CD writing.");
            if (!(bool)format.IsCurrentMediaSupported(recorder))
                throw new InvalidOperationException("Insert a blank CD-R (or erased CD-RW).");

            DynamicCdRawSession session = new((object)format, args => ReportWrite(args, totalBytes, progress));
            Report("media", "Preparing and locking the CD…");
            session.PrepareMedia();
            prepared = true;
            if (!session.MediaPhysicallyBlank) throw new InvalidOperationException("The CD is not blank; burn cancelled.");
            int sectorType = WindowsImapiBurner.ResolveCdDaoSectorType(session.SupportedSectorTypes);
            session.SetRequestedSectorType(sectorType);
            if (request.WriteSpeed is { } speed) session.SetWriteSpeed(speed.SectorsPerSecond, speed.RotationTypeIsPureCav);
            else session.SetWriteSpeed(WindowsImapiBurner.CdDaoFallbackSpeedSectorsPerSecond, false);

            Report("buffer", "Building the disc image (hidden pre-gap + songs)…");
            long expectedWithGaps = artSectors + trackSectors.Sum() + ((trackSectors.Length - 1) * StandardPregapSectors);
            long expectedGapless = artSectors + trackSectors.Sum();
            long expected = request.Gapless ? expectedGapless : expectedWithGaps;
            // IMAPI documents DisableGaplessAudio contradictorily, so build with the documented
            // meaning (false = 2-second gaps) and, if the resulting lead-out says otherwise,
            // rebuild with the flag flipped. Both layouts are the same length for one song.
            bool disableGapless = request.Gapless;
            (dynamic creator, IStream image, long startOfLeadout) = CreateImage(request, sectorType, disableGapless, streams);
            if (trackSectors.Length > 1 && startOfLeadout != expected
                && startOfLeadout == (request.Gapless ? expectedWithGaps : expectedGapless))
            {
                ImapiCom.ReleaseComObject(creator);
                DisposeAll(streams);
                (creator, image, startOfLeadout) = CreateImage(request, sectorType, !disableGapless, streams);
            }

            Report("diagnostic", $"Hidden pre-gap image: art {artSectors} sectors, {trackSectors.Length} songs, "
                + $"gaps={(request.Gapless ? "none" : "2 s")}, StartOfLeadout={startOfLeadout} (expected {expected}), "
                + $"LastPossibleStartOfLeadout={session.LastPossibleStartOfLeadout}.");
            long excess = startOfLeadout - session.LastPossibleStartOfLeadout;
            if (excess > 0)
                throw new InvalidOperationException($"The layout is {excess} sectors larger than this disc; burn cancelled.");

            cancellationToken.ThrowIfCancellationRequested();
            using CancellationTokenRegistration registration = cancellationToken.Register(session.CancelWrite);
            Report("write", "Writing the CD…");
            session.WriteMedia(image);
            Report("finalize", "Closing the disc…", totalBytes);
            session.ReleaseMedia();
            prepared = false;
            ImapiCom.ReleaseComObject(creator);
            Report("complete", "Hidden pre-gap CD burned.", totalBytes);
        }
        finally
        {
            if (prepared && format is not null)
            {
                try { format.ReleaseMedia(); }
                catch (COMException) { /* Keep the original failure. */ }
            }

            DisposeAll(streams);
            if (format is not null) ImapiCom.ReleaseComObject((object)format);
            ImapiCom.ReleaseComObject((object)recorder);
        }
    }

    private static (dynamic Creator, IStream Image, long StartOfLeadout) CreateImage(HiddenPregapCdRequest request,
        int sectorType, bool disableGaplessAudio, List<IDisposable> streams)
    {
        dynamic creator = ImapiCom.CreateComObject("IMAPI2.MsftRawCDImageCreator");
        creator.ResultingImageType = sectorType;
        creator.DisableGaplessAudio = disableGaplessAudio;
        PcmFileComStream pregap = new(request.Pregap);
        streams.Add(pregap);
        creator.AddSpecialPregap(pregap);
        foreach (CdAudioFileRegion track in request.Tracks)
        {
            PcmFileComStream stream = new(track);
            streams.Add(stream);
            creator.AddTrack(AudioSectorType, stream);
        }

        IStream image = (IStream)creator.CreateResultImage();
        return (creator, image, Convert.ToInt64(creator.StartOfLeadout));
    }

    private static void ReportWrite(object args, long totalBytes, IProgress<OpticalBurnProgress>? progress)
    {
        if (progress is null) return;
        try
        {
            dynamic update = args;
            long start = Convert.ToInt64(update.StartLba);
            long count = Convert.ToInt64(update.SectorCount);
            long written = Convert.ToInt64(update.LastWrittenLba) - start;
            if (count <= 0) return;
            double fraction = Math.Clamp((double)written / count, 0, 1);
            progress.Report(new OpticalBurnProgress("write",
                $"Writing the CD… {fraction:P0}", (long)(fraction * totalBytes), totalBytes));
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException
                                              or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Progress events are optional telemetry.
        }
    }

    private static long SectorsOf(CdAudioFileRegion region) => (region.Length + SectorBytes - 1) / SectorBytes;

    private static void DisposeAll(List<IDisposable> streams)
    {
        foreach (IDisposable stream in streams) stream.Dispose();
        streams.Clear();
    }

    /// <summary>Read-only COM stream over PCM bytes in a WAV file, padded with silence to a
    /// whole number of sectors as IMAPI requires.</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class PcmFileComStream : IStream, IDisposable
    {
        private readonly FileStream _file;
        private readonly long _offset;
        private readonly long _dataLength;
        private readonly long _length;
        private long _position;

        internal PcmFileComStream(CdAudioFileRegion region)
        {
            _file = new FileStream(region.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
            _offset = region.Offset;
            _dataLength = region.Length;
            _length = SectorsOf(region) * SectorBytes;
            if (_offset < 0 || _offset + _dataLength > _file.Length)
                throw new InvalidDataException($"'{region.Path}' is shorter than its audio data.");
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            ArgumentNullException.ThrowIfNull(pv);
            int count = (int)Math.Clamp(Math.Min(cb, _length - _position), 0, pv.Length);
            int fromFile = (int)Math.Clamp(Math.Min(count, _dataLength - _position), 0, count);
            if (fromFile > 0)
            {
                _file.Position = _offset + _position;
                _file.ReadExactly(pv, 0, fromFile);
            }

            if (count > fromFile) Array.Clear(pv, fromFile, count - fromFile);
            _position += count;
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, count);
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long target = dwOrigin switch
            {
                0 => dlibMove,
                1 => checked(_position + dlibMove),
                2 => checked(_length + dlibMove),
                _ => -1,
            };
            if (target < 0 || target > _length)
                throw new COMException("Invalid stream position.", unchecked((int)0x80030001));
            _position = target;
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, _position);
        }

        public void Stat(out STATSTG pstatstg, int grfStatFlag) => pstatstg = new STATSTG { type = 2, cbSize = _length };

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new NotSupportedException();
        public void SetSize(long libNewSize) => throw new NotSupportedException();
        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotSupportedException();
        public void Commit(int grfCommitFlags) { }
        public void Revert() => throw new NotSupportedException();
        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();
        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();
        public void Clone(out IStream ppstm) => throw new NotSupportedException();
        public void Dispose() => _file.Dispose();
    }
}
