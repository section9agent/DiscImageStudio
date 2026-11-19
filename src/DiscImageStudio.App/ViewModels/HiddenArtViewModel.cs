using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Burning;
using DiscImageStudio.Cd;
using DiscImageStudio.Core;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;

namespace DiscImageStudio.ViewModels;

/// <summary>One song in the hidden-art music list.</summary>
public sealed record HiddenArtTrackItem(CdMusicTrack Track)
{
    public string Name => Path.GetFileName(Track.Path);

    public string DurationText => Track.Duration.ToString(Track.Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss",
        CultureInfo.InvariantCulture);
}

/// <summary>Export-page card for music plus hidden art on one audio CD: art in track 1's
/// pre-gap (burned with IMAPI2) or in a second session (burned with cdrecord). The picture,
/// geometry and tone come from the project like a normal CD export.</summary>
public partial class HiddenArtViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly DiscParametersState _state;
    private readonly Func<CdDiscParameters> _parameters;
    private readonly Func<Task<string>> _composite;

    internal HiddenArtViewModel(ShellViewModel shell, DiscParametersState state,
        Func<CdDiscParameters> parameters, Func<Task<string>> composite)
    {
        _shell = shell;
        _state = state;
        _parameters = parameters;
        _composite = composite;
        AddMusicCommand = new RelayCommand(AddMusic, CanEdit);
        RemoveMusicCommand = new RelayCommand(RemoveMusic, () => CanEdit() && SelectedTrack is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => CanEdit() && SelectedIndex > 0);
        MoveDownCommand = new RelayCommand(() => Move(1), () => CanEdit() && SelectedIndex >= 0 && SelectedIndex < Tracks.Count - 1);
        ClearMusicCommand = new RelayCommand(() => { Tracks.Clear(); UpdateLayout(); }, () => CanEdit() && Tracks.Count > 0);
        BrowseCdRecordCommand = new RelayCommand(BrowseCdRecord, CanEdit);
        ScanDevicesCommand = new AsyncRelayCommand(ScanDevicesAsync, CanEdit);
        ReadSessionStartCommand = new AsyncRelayCommand(ReadSessionStartAsync, CanEdit);
        BurnCommand = new AsyncRelayCommand(BurnAsync, CanEdit);
        _shell.BusyChanged += NotifyCommands;
        _state.PropertyChanged += (_, _) => UpdateLayout();
        CdRecordPath = CdRecord.Locate() ?? string.Empty;
        UpdateLayout();
    }

    public ObservableCollection<HiddenArtTrackItem> Tracks { get; } = [];

    public ObservableCollection<CdRecordDevice> CdRecordDevices { get; } = [];

    public ObservableCollection<OpticalBurnDevice> ImapiDevices { get; } = [];

    public RelayCommand AddMusicCommand { get; }
    public RelayCommand RemoveMusicCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand ClearMusicCommand { get; }
    public RelayCommand BrowseCdRecordCommand { get; }
    public IAsyncRelayCommand ScanDevicesCommand { get; }
    public IAsyncRelayCommand ReadSessionStartCommand { get; }
    public IAsyncRelayCommand BurnCommand { get; }

    /// <summary>0 = whole disc (original behaviour), 1 = hidden pre-gap, 2 = second session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placement), nameof(IsHidden), nameof(IsPregap), nameof(IsSecondSession),
        nameof(BurnButtonText))]
    private int _placementIndex;

    [ObservableProperty]
    private bool _twoSecondGaps = true;

    /// <summary>Outer edge of the pre-gap art ring.</summary>
    [ObservableProperty]
    private string _pregapArtRadius = "32";

    /// <summary>Optional outer edge of the session 2 art ring; empty fills to the disc edge.</summary>
    [ObservableProperty]
    private string _sessionArtRadius = string.Empty;

    /// <summary>Optional measured session 2 start (cdrecord -msinfo); empty = estimate.</summary>
    [ObservableProperty]
    private string _sessionStart = string.Empty;

    [ObservableProperty]
    private string _layoutSummary = string.Empty;

    [ObservableProperty]
    private bool _isLayoutValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveMusicCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private HiddenArtTrackItem? _selectedTrack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private string _cdRecordPath = string.Empty;

    [ObservableProperty]
    private CdRecordDevice? _selectedCdRecordDevice;

    [ObservableProperty]
    private OpticalBurnDevice? _selectedImapiDevice;

    /// <summary>Optional write speed multiplier (e.g. 8); empty = drive default / IMAPI fallback.</summary>
    [ObservableProperty]
    private string _writeSpeed = string.Empty;

    /// <summary>cdrecord -dummy: the laser stays off, so nothing is written.</summary>
    [ObservableProperty]
    private bool _testMode;

    [ObservableProperty]
    private string _deviceStatus = "Press Scan to find recorders.";

    public CdArtPlacement Placement => PlacementIndex switch
    {
        1 => CdArtPlacement.HiddenPregap,
        2 => CdArtPlacement.SecondSession,
        _ => CdArtPlacement.WholeDisc,
    };

    public bool IsHidden => Placement != CdArtPlacement.WholeDisc;

    public bool IsPregap => Placement == CdArtPlacement.HiddenPregap;

    public bool IsSecondSession => Placement == CdArtPlacement.SecondSession;

    public string BurnButtonText => IsSecondSession ? "Burn both sessions (cdrecord)" : "Burn disc (Windows IMAPI)";

    partial void OnPlacementIndexChanged(int value) => UpdateLayout();
    partial void OnTwoSecondGapsChanged(bool value) => UpdateLayout();
    partial void OnPregapArtRadiusChanged(string value) => UpdateLayout();
    partial void OnSessionArtRadiusChanged(string value) => UpdateLayout();
    partial void OnSessionStartChanged(string value) => UpdateLayout();

    /// <summary>Builds the layout from the current inputs; throws with a readable message.</summary>
    internal CdHiddenArtPlan BuildPlan(CdDiscParameters parameters, long? measuredSessionStart = null)
    {
        CdMusicTrack[] music = Tracks.Select(item => item.Track).ToArray();
        long gap = TwoSecondGaps ? CdHiddenArtPlan.StandardPregapSectors : 0;
        if (Placement == CdArtPlacement.HiddenPregap)
            return CdHiddenArtPlan.ForPregap(parameters, ParseRadius(PregapArtRadius), music, gap);
        if (Placement != CdArtPlacement.SecondSession)
            throw new InvalidOperationException("Choose a hidden-art placement first.");
        long? start = measuredSessionStart ?? (string.IsNullOrWhiteSpace(SessionStart) ? null
            : long.TryParse(SessionStart.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value > 0
                ? value : throw new ArgumentException("The session 2 start sector must be a positive whole number."));
        double? radius = string.IsNullOrWhiteSpace(SessionArtRadius) ? null : ParseRadius(SessionArtRadius);
        return CdHiddenArtPlan.ForSecondSession(parameters, music, gap, start, radius);
    }

    /// <summary>Export: writes the art WAV plus its CUE sheet or cdrecord script.</summary>
    internal async Task<bool> ExportAsync(string compositePath, string output)
    {
        CdDiscParameters parameters = _parameters();
        CdHiddenArtPlan plan = BuildPlan(parameters);
        return await RunGenerationAsync(compositePath, output, parameters, plan);
    }

    private Task<bool> RunGenerationAsync(string compositePath, string output, CdDiscParameters parameters,
        CdHiddenArtPlan plan)
    {
        ToneProfile tone = _state.Tone;
        bool interleave = _state.CdInterleave;
        return _shell.RunImageJobAsync(
            plan.Placement == CdArtPlacement.HiddenPregap ? "Generating hidden pre-gap art…" : "Generating session 2 art…",
            output,
            (progress, token) =>
            {
                CdHiddenArtResult result = CdHiddenArt.Generate(compositePath, output, parameters, plan, tone, interleave,
                    value => progress.Report(new DiscJobProgress(CdDiscModule.ModuleId, "generate",
                        $"Generating art {value.Fraction:P1}", value.Fraction, value.CompletedBytes, value.TotalBytes,
                        value.Elapsed)), token);
                string files = result.CueSheetPath is not null ? $"CUE sheet: {result.CueSheetPath}"
                    : $"cdrecord script: {result.CdRecordScriptPath}";
                return new DiscImageResult(result.ArtWavePath, result.Summary + "\n" + files);
            },
            plan.Describe(parameters));
    }

    private bool CanEdit() => !_shell.IsBusy;

    private void NotifyCommands()
    {
        AddMusicCommand.NotifyCanExecuteChanged();
        RemoveMusicCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        ClearMusicCommand.NotifyCanExecuteChanged();
        BrowseCdRecordCommand.NotifyCanExecuteChanged();
        ScanDevicesCommand.NotifyCanExecuteChanged();
        ReadSessionStartCommand.NotifyCanExecuteChanged();
        BurnCommand.NotifyCanExecuteChanged();
    }

    private void AddMusic()
    {
        string[]? files = _shell.Dialogs?.PickOpenMany("Add music (44.1 kHz 16-bit stereo WAV)",
            "WAV audio|*.wav|All files|*.*");
        if (files is null) return;
        List<string> problems = [];
        foreach (string file in files)
        {
            try { Tracks.Add(new HiddenArtTrackItem(CdMusicTrack.Load(file))); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        if (problems.Count > 0)
            _shell.ShowToast("Some files were not added", string.Join("\n", problems)
                + "\nConvert them to 44.1 kHz, 16-bit stereo PCM WAV first.", ToastKind.Warning);
        UpdateLayout();
        ClearMusicCommand.NotifyCanExecuteChanged();
    }

    private void RemoveMusic()
    {
        if (SelectedTrack is null) return;
        int index = SelectedIndex;
        Tracks.Remove(SelectedTrack);
        SelectedIndex = Math.Min(index, Tracks.Count - 1);
        UpdateLayout();
        ClearMusicCommand.NotifyCanExecuteChanged();
    }

    private void Move(int direction)
    {
        int index = SelectedIndex;
        int target = index + direction;
        if (index < 0 || target < 0 || target >= Tracks.Count) return;
        Tracks.Move(index, target);
        SelectedIndex = target;
        UpdateLayout();
    }

    private void BrowseCdRecord()
    {
        string? picked = _shell.Dialogs?.PickOpen("Locate cdrecord", "cdrecord|cdrecord.exe;wodim.exe|Programs|*.exe|All files|*.*");
        if (picked is not null) CdRecordPath = picked;
    }

    private void UpdateLayout()
    {
        if (!IsHidden)
        {
            IsLayoutValid = false;
            LayoutSummary = string.Empty;
            return;
        }

        try
        {
            CdDiscParameters parameters = _parameters();
            CdHiddenArtPlan plan = BuildPlan(parameters);
            LayoutSummary = plan.Describe(parameters);
            IsLayoutValid = true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException
                                              or InvalidOperationException)
        {
            LayoutSummary = exception.Message;
            IsLayoutValid = false;
        }
    }

    private static double ParseRadius(string text)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
           && double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentException("Enter the art ring's outer radius in millimetres, e.g. 32.");

    private int? ParseSpeed()
    {
        if (string.IsNullOrWhiteSpace(WriteSpeed)) return null;
        string text = WriteSpeed.Trim().TrimEnd('x', 'X', '×');
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value is > 0 and <= 52
            ? value
            : throw new ArgumentException("Write speed must be a CD multiplier between 1 and 52, e.g. 8.");
    }

    // ---- Devices ----

    private async Task ScanDevicesAsync()
    {
        try
        {
            if (IsSecondSession)
            {
                string cdrecord = RequireCdRecord();
                DeviceStatus = "Scanning with cdrecord -scanbus…";
                CdRecordRunResult result = await CdRecord.RunAsync(cdrecord, CdRecord.ScanbusArguments());
                CdRecordDevices.Clear();
                foreach (CdRecordDevice device in CdRecord.ParseScanbus(result.Output)) CdRecordDevices.Add(device);
                SelectedCdRecordDevice = CdRecordDevices.FirstOrDefault();
                DeviceStatus = CdRecordDevices.Count == 0
                    ? "cdrecord found no recorders. Output:\n" + result.Output.Trim()
                    : $"cdrecord found {CdRecordDevices.Count} recorder(s).";
            }
            else
            {
                DeviceStatus = "Looking for recorders…";
                IReadOnlyList<OpticalBurnDevice> devices = await _shell.OpticalBurner.GetDevicesAsync();
                ImapiDevices.Clear();
                foreach (OpticalBurnDevice device in devices) ImapiDevices.Add(device);
                SelectedImapiDevice = ImapiDevices.FirstOrDefault();
                DeviceStatus = ImapiDevices.Count == 0 ? "No recorders found." : $"Found {ImapiDevices.Count} recorder(s).";
            }
        }
        catch (Exception exception)
        {
            DeviceStatus = "Recorder scan failed: " + LibraryText.Translate(exception.Message);
        }
    }

    private async Task ReadSessionStartAsync()
    {
        try
        {
            string cdrecord = RequireCdRecord();
            CdRecordDevice device = SelectedCdRecordDevice ?? throw new ArgumentException("Scan for recorders and pick one first.");
            CdRecordRunResult result = await CdRecord.RunAsync(cdrecord, CdRecord.MsinfoArguments(device.Address));
            (_, long next) = CdRecord.ParseMsinfo(result.Output);
            SessionStart = next.ToString(CultureInfo.InvariantCulture);
            _shell.AppendLog($"[{DateTime.Now:HH:mm:ss}] cdrecord -msinfo: session 2 starts at sector {next}.\n");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _shell.ShowToast("Could not read the session address", LibraryText.Translate(exception.Message), ToastKind.Warning);
        }
    }

    private string RequireCdRecord()
    {
        string? path = CdRecord.Locate(CdRecordPath);
        if (path is null)
            throw new ArgumentException("cdrecord was not found. Put cdrecord.exe (cdrtools) in a \"cdrtools\" folder next to "
                + "DiscImageStudio.exe, add it to PATH, or pick it with Browse.");
        CdRecordPath = path;
        return path;
    }

    // ---- Burning ----

    private async Task BurnAsync()
    {
        try
        {
            if (IsPregap) await BurnPregapAsync();
            else if (IsSecondSession) await BurnSecondSessionAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException
                                              or OverflowException)
        {
            _shell.ShowToast("Check the hidden-art settings", LibraryText.Translate(exception.Message), ToastKind.Warning);
        }
    }

    private string RequireOutput()
    {
        string output = _state.CdOutputPath.Trim();
        if (output.Length == 0 || !Path.GetExtension(output).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a .wav output file above first; the art track is saved there before burning.");
        return output;
    }

    private async Task BurnPregapAsync()
    {
        OpticalBurnDevice device = SelectedImapiDevice ?? throw new ArgumentException("Scan for recorders and pick one first.");
        string output = RequireOutput();
        CdDiscParameters parameters = _parameters();
        CdHiddenArtPlan plan = BuildPlan(parameters);
        int? speed = ParseSpeed();
        if (!await _shell.ConfirmAsync("Burn hidden pre-gap CD",
                $"{plan.Describe(parameters)}\n\nRecorder: {device.DisplayName}\nInsert a blank CD-R. The disc is closed "
                + "when finished.\n\nNote: rewinding before track 1 on a player plays the art as loud noise.",
                "Burn"))
            return;

        string composite = await _composite();
        if (!await RunGenerationAsync(composite, output, parameters, plan)) return;

        CdAudioFileRegion pregap = new(output, 44, plan.PregapFileSectors * CdDiscParameters.BytesPerSector);
        HiddenPregapCdRequest request = new(device.Id, pregap,
            plan.Music.Select(track => new CdAudioFileRegion(track.Path, track.AudioOffset, track.AudioBytes)).ToArray(),
            Gapless: plan.MusicGapSectors == 0,
            WriteSpeed: speed is { } multiplier ? new OpticalWriteSpeed(multiplier * 75, false) : null);
        await RunBurnStepAsync("Burning hidden pre-gap CD", async report =>
            await HiddenPregapCdBurner.BurnAsync(request, new Progress<OpticalBurnProgress>(value =>
            {
                if (value.Stage == "diagnostic") _shell.AppendLog($"[{DateTime.Now:HH:mm:ss}] {value.Message}\n");
                else report(value.Message, value.Fraction * 100);
            })));
    }

    private async Task BurnSecondSessionAsync()
    {
        string cdrecord = RequireCdRecord();
        CdRecordDevice device = SelectedCdRecordDevice ?? throw new ArgumentException("Scan for recorders and pick one first.");
        string output = RequireOutput();
        CdDiscParameters parameters = _parameters();
        CdHiddenArtPlan estimate = BuildPlan(parameters);
        int? speed = ParseSpeed();
        bool dummy = TestMode;
        if (!await _shell.ConfirmAsync("Burn music + second-session art",
                $"{estimate.Describe(parameters)}\n\nRecorder: {device} via cdrecord\n"
                + (dummy ? "TEST MODE: the laser stays off, nothing is written. Only session 1 is simulated.\n"
                    : "Insert a blank CD-R. Session 1 (music) is burned and left open, cdrecord reports where session 2 "
                      + "starts, the art is generated for exactly that address, then session 2 is burned and the disc closed.\n")
                + "Do not remove the disc between the steps.",
                dummy ? "Simulate" : "Burn"))
            return;

        string composite = await _composite();
        bool ok = await RunBurnStepAsync(dummy ? "Simulating session 1 (cdrecord -dummy)" : "Burning session 1: music",
            report => RunCdRecordAsync(cdrecord, CdRecord.SessionOneArguments(device.Address, estimate, speed, dummy), report));
        if (!ok) return;
        if (dummy)
        {
            _shell.ShowToast("Simulation finished", "Session 1 simulated successfully. Turn off test mode to burn for real.",
                ToastKind.Success);
            return;
        }

        CdRecordRunResult msinfo = await CdRecord.RunAsync(cdrecord, CdRecord.MsinfoArguments(device.Address));
        (_, long next) = CdRecord.ParseMsinfo(msinfo.Output);
        SessionStart = next.ToString(CultureInfo.InvariantCulture);
        _shell.AppendLog($"[{DateTime.Now:HH:mm:ss}] cdrecord -msinfo: session 2 starts at sector {next} "
                         + $"(estimate was {estimate.ArtStartSector}).\n");
        CdHiddenArtPlan plan = BuildPlan(parameters, next);
        if (!await RunGenerationAsync(composite, output, parameters, plan)) return;
        if (await RunBurnStepAsync("Burning session 2: art",
                report => RunCdRecordAsync(cdrecord, CdRecord.SessionTwoArguments(device.Address, output, speed), report)))
        {
            _shell.ShowToast("Disc finished", "Music in session 1, art in session 2. The disc is closed.", ToastKind.Success);
        }
    }

    private async Task RunCdRecordAsync(string cdrecord, IReadOnlyList<string> arguments, Action<string, double> report)
    {
        _shell.AppendLog($"[{DateTime.Now:HH:mm:ss}] {Path.GetFileName(cdrecord)} {CdRecord.Join(arguments)}\n");
        DateTime lastProgressLog = DateTime.MinValue;
        CdRecordRunResult result = await CdRecord.RunAsync(cdrecord, arguments, line =>
        {
            _shell.ReportOnDispatcher(() =>
            {
                Match progress = CdRecordProgress().Match(line);
                if (progress.Success)
                {
                    double done = double.Parse(progress.Groups["done"].Value, CultureInfo.InvariantCulture);
                    double total = double.Parse(progress.Groups["total"].Value, CultureInfo.InvariantCulture);
                    report(line.Trim(), total > 0 ? done / total * 100 : 0);
                    if (DateTime.UtcNow - lastProgressLog < TimeSpan.FromSeconds(10)) return;
                    lastProgressLog = DateTime.UtcNow;
                }

                _shell.AppendLog(line + "\n");
            });
        });
        if (!result.Succeeded)
            throw new InvalidOperationException($"cdrecord exited with code {result.ExitCode}. See the job log for its output.");
    }

    /// <summary>Runs one burn step in the job dock; returns false (after reporting) on failure.</summary>
    private async Task<bool> RunBurnStepAsync(string title, Func<Action<string, double>, Task> step)
    {
        if (_shell.IsBusy) return false;
        _shell.SetBusy(true);
        _shell.BeginDockJob(title, determinate: true);
        _shell.AppendLog($"\n[{DateTime.Now:HH:mm:ss}] {title}\n");
        try
        {
            await step((status, percent) => _shell.ReportDockJob(status, percent));
            _shell.CompleteDockJob("Done: " + title);
            _shell.AppendLog($"[{DateTime.Now:HH:mm:ss}] Done: {title}\n");
            return true;
        }
        catch (Exception exception)
        {
            _shell.AbortDockJob("Failed: " + title, expandLog: true);
            _shell.AppendLog(JobDiagnostics.FormatLog(title, exception));
            _shell.ShowLogToast("Burn step failed", LibraryText.Translate(exception.Message), ToastKind.Error);
            return false;
        }
        finally
        {
            _shell.SetBusy(false);
        }
    }

    [GeneratedRegex(@"(?<done>\d+(?:\.\d+)?)\s+of\s+(?<total>\d+(?:\.\d+)?)\s+MB written", RegexOptions.IgnoreCase)]
    private static partial Regex CdRecordProgress();
}
