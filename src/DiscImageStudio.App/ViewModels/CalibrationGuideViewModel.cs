using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiscImageStudio.ViewModels;

/// <summary>One stop on the calibration guide's progress rail.</summary>
public sealed record CalibrationGuideStep(string Label, bool IsDone, bool IsActive)
{
    public string Glyph => IsDone ? "✓" : IsActive ? "●" : "○";
}

/// <summary>Fork addition: a plain-language guide across the top of the calibration page. It
/// reads the page's existing state and says, in one sentence, what to do next, with a button
/// that runs that action. It never replaces the step cards below; it points at them.</summary>
public partial class CalibrationViewModel
{
    private static readonly string[] GuideStepLabels =
        ["Make test disc", "Burn it", "Photograph", "Check edges", "Measure", "Apply"];

    /// <summary>The guide banner can be hidden for people who know the workflow.</summary>
    [ObservableProperty] private bool _showGuide = true;

    public RelayCommand HideGuideCommand => _hideGuideCommand ??= new RelayCommand(() => ShowGuide = false);

    public RelayCommand ShowGuideCommand => _showGuideCommand ??= new RelayCommand(() => ShowGuide = true);

    private RelayCommand? _hideGuideCommand;
    private RelayCommand? _showGuideCommand;

    private int GuidePhase
    {
        get
        {
            if (_target is null) return 0;
            if (PhotoImage is null) return 1;
            if (IsPhotoStep) return IsRectificationReady ? 3 : 2;
            return HasGuideResult ? 5 : 4;
        }
    }

    private bool HasGuideResult => IsRingStep ? CanApplyCoarse : _fit is { Succeeded: true };

    private bool CanApplyCoarse => ApplyCoarseParametersCommand?.CanExecute(null) == true;

    public int GuideRound => IsFineMode ? 3 : IsIntermediateMode ? 2 : 1;

    public string GuideRoundTitle => GuideRound switch
    {
        1 => "Round 1 of 3 · Rough size",
        2 => "Round 2 of 3 · Refine edges",
        _ => "Round 3 of 3 · Straighten lines",
    };

    public IReadOnlyList<CalibrationGuideStep> GuideSteps
    {
        get
        {
            int phase = GuidePhase;
            // "Burn it" and "Photograph" share a phase: the app cannot see the burn happen.
            int Map(int step) => step <= 1 ? step : step - 1;
            return GuideStepLabels.Select((label, step) =>
            {
                int stepPhase = Map(step);
                return new CalibrationGuideStep(label, stepPhase < phase, stepPhase == phase);
            }).ToArray();
        }
    }

    public string GuideInstruction => GuidePhase switch
    {
        0 => GuideRound switch
        {
            1 => "Start here. Pick your disc type and preset in step 1, then make a test disc. "
                 + "This round finds where the recording area starts and ends.",
            2 => "Make the round 2 test disc. It burns wavy band edges that pin down the track spacing.",
            _ => "Make the round 3 test disc. It burns a thin cross so the last bit of curvature can be straightened out.",
        },
        1 => "Burn the test disc on a blank disc from the same batch, then photograph the whole disc, rim and centre "
             + "hole included, against a plain background. Import the photo when you have it.",
        2 => (PhotoEdgesFound
                 ? "The disc's outer edge and centre hole were found automatically. "
                 : string.IsNullOrEmpty(PhotoDetectionMessage) ? string.Empty : PhotoDetectionMessage + " ")
             + "Check that the cyan points sit on the outer edge and the orange points on the thin edge of the hole "
             + "(zoom in with the mouse wheel), drag any that are off, then straighten the photo.",
        3 => "Check the straightened photo: the cyan circle should hug the rim and the orange circle the hole. "
             + "If it looks right, confirm it.",
        4 => GuideRound switch
        {
            1 => "Mark the six band edges along one line from the centre outward, as the measuring card on the right "
                 + "describes, then solve.",
            2 => "Click on each wavy band edge. With snapping on, one click follows the whole edge; click again past "
                 + "any spot where it stopped. Then solve.",
            _ => "Click along the thin cross line from near the centre to the outside; each click snaps onto the line. "
                 + "About a dozen spread-out points is enough. Then solve.",
        },
        _ => GuideRound < 3
            ? "Result ready. Apply it so the next test disc uses the new values, then go on to the next round."
            : "Result ready. Apply it, then use Save as preset (top right) so your designs use it.",
    };

    public string GuidePrimaryLabel => GuidePhase switch
    {
        0 => "Make test disc",
        1 => "Import photo…",
        2 => "Straighten photo",
        3 => "Confirm",
        4 => "Solve",
        _ => "Apply result",
    };

    public ICommand? GuidePrimaryCommand => GuidePhase switch
    {
        0 => CreateTargetCommand,
        1 => ImportPhotoCommand,
        2 => RectifyPhotoCommand,
        3 => ConfirmRectificationCommand,
        4 => IsRingStep ? SolveRingsCommand : SolveCommand,
        _ => IsRingStep ? ApplyCoarseParametersCommand : ApplyParametersCommand,
    };

    public string GuideSecondaryLabel => GuidePhase switch
    {
        1 => "Burn test disc…",
        5 when GuideRound < 3 => "Next round",
        _ => string.Empty,
    };

    public ICommand? GuideSecondaryCommand => GuidePhase switch
    {
        1 => StreamBurnCommand,
        5 when GuideRound == 1 => SelectIntermediateModeCommand,
        5 when GuideRound == 2 => SelectFineModeCommand,
        _ => null,
    };

    public bool HasGuideSecondary => GuideSecondaryCommand is not null;

    /// <summary>The snap toggle only matters while measuring lines and edges.</summary>
    public bool GuideShowsSnap => GuidePhase == 4 && IsLineFitStep;

    private static readonly string[] GuideProperties =
    [
        nameof(GuideRound), nameof(GuideRoundTitle), nameof(GuideSteps), nameof(GuideInstruction),
        nameof(GuidePrimaryLabel), nameof(GuidePrimaryCommand), nameof(GuideSecondaryLabel),
        nameof(GuideSecondaryCommand), nameof(HasGuideSecondary), nameof(GuideShowsSnap),
    ];

    private static readonly HashSet<string> GuideInputs =
    [
        nameof(PhotoImage), nameof(IsPhotoStep), nameof(IsRectificationReady), nameof(IsCoarseMode),
        nameof(IsIntermediateMode), nameof(IsFineMode), nameof(PhotoEdgesFound), nameof(PhotoDetectionMessage),
        nameof(StepOneState), nameof(StepThreeState), nameof(TracePoints), nameof(CoarseSummary),
        nameof(RingSolveLabel), nameof(IsBusy),
    ];

    private bool _notifyingGuide;

    /// <summary>Called from the constructor: re-reads the guide whenever an input changes.</summary>
    private void InitializeGuide() => PropertyChanged += OnGuideInputChanged;

    private void OnGuideInputChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_notifyingGuide || args.PropertyName is null || !GuideInputs.Contains(args.PropertyName)) return;
        NotifyGuide();
    }

    private void NotifyGuide()
    {
        if (_notifyingGuide) return;
        _notifyingGuide = true;
        try
        {
            foreach (string property in GuideProperties) OnPropertyChanged(property);
        }
        finally
        {
            _notifyingGuide = false;
        }
    }
}
