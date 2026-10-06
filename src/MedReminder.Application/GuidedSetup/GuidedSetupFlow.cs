using MedReminder.Domain.Notifications;

namespace MedReminder.Application.GuidedSetup;

// Guided setup (docs/prompt/PROMPT-GUIDED-SETUP.md): the steps of the
// window that takes a profile from an empty list to medicines that warn.
// The window (GuidedSetupForm) only shows the step this class says and
// passes the answers in; the order, the skips and the decisions that do
// not need a screen live here so they are tested without WinForms.
public enum GuidedSetupStep
{
    // "For me" or "For someone I look after", and the profile name.
    Audience,
    // The medicines added through the existing medicine dialog.
    Medicines,
    // Lead time and channel of the warnings.
    Warning,
    // The user's and the second person's address. Shown only when the
    // warning step includes email or the medicines are for someone else.
    Email,
    Summary,
}

public enum GuidedSetupAudience
{
    Myself,
    SomeoneElse,
}

public enum GuidedSetupOutcome
{
    // Still open.
    None,
    // Finish on the summary.
    Completed,
    // "Not now", Esc or the window closed.
    Dismissed,
}

// What the email step says about the SMTP account.
public enum EmailAccountState
{
    Configured,
    // An administrator gets a button that opens Settings on Email.
    AdministratorCanSetUp,
    // A standard user is told that an administrator has to set it up.
    AdministratorRequired,
}

// What the flow depends on and cannot ask the user: read by the window
// when it opens. SmtpConfigured is read again after Settings closes
// (GuidedSetupFlow.SetSmtpConfigured). SendsEmail is false on a device
// of an installation whose master is another device (IMasterRole).
public sealed record GuidedSetupEnvironment(bool IsAdministrator, bool SmtpConfigured, bool SendsEmail);

// Lead time and channels given to a medicine created in the medicine
// dialog: the defaults of the dialog until the guided setup stores the
// user's choice (device-local, profiles\<id>\ui.settings.json).
public sealed record NewMedicineDefaults(int ThresholdDays, NotificationChannels Channels)
{
    public const int MinThresholdDays = 0;
    // The range of the threshold field of the medicine dialog.
    public const int MaxThresholdDays = 365;

    public static NewMedicineDefaults BuiltIn { get; } = new(7, NotificationChannels.Windows);

    // A value read from a file: null when out of range or without any
    // channel, so a damaged file falls back to the built-in values.
    public static NewMedicineDefaults? TryCreate(int thresholdDays, NotificationChannels channels)
        => thresholdDays is < MinThresholdDays or > MaxThresholdDays
           || (channels & NotificationChannels.Both) == NotificationChannels.None
           || (channels & ~NotificationChannels.Both) != NotificationChannels.None
            ? null
            : new NewMedicineDefaults(thresholdDays, channels);
}

// The two addresses of the email step, mapped to the profile settings.
public sealed record GuidedSetupAddresses(string ToAddress, string CaregiverAddress);

public sealed class GuidedSetupFlow
{
    // The three preset lead times of the warning step; any other value
    // in the range is a custom one.
    public static readonly IReadOnlyList<int> LeadTimePresets = [7, 10, 14];

    private static readonly GuidedSetupStep[] AllSteps =
    [
        GuidedSetupStep.Audience,
        GuidedSetupStep.Medicines,
        GuidedSetupStep.Warning,
        GuidedSetupStep.Email,
        GuidedSetupStep.Summary,
    ];

    private readonly List<Guid> _addedMedicines = [];
    private int _leadDays;
    private NotificationChannels _channels;

    public GuidedSetupFlow(GuidedSetupEnvironment environment, NewMedicineDefaults? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        IsAdministrator = environment.IsAdministrator;
        SmtpConfigured = environment.SmtpConfigured;
        SendsEmail = environment.SendsEmail;
        var start = defaults ?? NewMedicineDefaults.BuiltIn;
        _leadDays = start.ThresholdDays;
        _channels = start.Channels;
    }

    public bool IsAdministrator { get; }
    public bool SmtpConfigured { get; private set; }
    public bool SendsEmail { get; }

    public GuidedSetupStep Current { get; private set; } = GuidedSetupStep.Audience;
    public GuidedSetupOutcome Outcome { get; private set; } = GuidedSetupOutcome.None;

    public GuidedSetupAudience Audience { get; set; } = GuidedSetupAudience.Myself;

    public int LeadDays
    {
        get => _leadDays;
        set
        {
            if (value is < NewMedicineDefaults.MinThresholdDays or > NewMedicineDefaults.MaxThresholdDays)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The lead time is out of range.");
            _leadDays = value;
        }
    }

    // Windows, Email or both; never none, or the medicines would not warn.
    public NotificationChannels Channels
    {
        get => _channels;
        set
        {
            if (NewMedicineDefaults.TryCreate(_leadDays, value) is null)
                throw new ArgumentOutOfRangeException(nameof(value), value, "At least one channel is required.");
            _channels = value;
        }
    }

    public NewMedicineDefaults Defaults => new(_leadDays, _channels);

    // The medicines created during this setup, in the order they were
    // added: the warning step is applied to these only.
    public IReadOnlyList<Guid> AddedMedicines => _addedMedicines;

    public void AddMedicine(Guid medicineId)
    {
        if (medicineId == Guid.Empty) throw new ArgumentException("A medicine id is required.", nameof(medicineId));
        if (!_addedMedicines.Contains(medicineId)) _addedMedicines.Add(medicineId);
    }

    public bool ShowsEmailStep
        => (_channels & NotificationChannels.Email) != 0 || Audience == GuidedSetupAudience.SomeoneElse;

    // The steps shown with the current answers, for the step indicator.
    public IReadOnlyList<GuidedSetupStep> Steps
        => [.. AllSteps.Where(s => s != GuidedSetupStep.Email || ShowsEmailStep)];

    // Zero-based position of the current step among Steps.
    public int CurrentIndex
    {
        get
        {
            var steps = Steps;
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i] == Current) return i;
            }
            // The email step stopped being shown while it was current:
            // impossible through Next and Back, which leave it first.
            return steps.Count - 1;
        }
    }

    public bool CanGoBack => Outcome == GuidedSetupOutcome.None && CurrentIndex > 0;
    public bool IsLastStep => Current == GuidedSetupStep.Summary;

    // Next step with the current answers. Every step can be left without
    // an answer: Next is also the skip.
    public GuidedSetupStep Next()
    {
        EnsureOpen();
        if (IsLastStep) throw new InvalidOperationException("The summary is the last step: finish instead.");
        var steps = Steps;
        Current = steps[CurrentIndex + 1];
        return Current;
    }

    public GuidedSetupStep Back()
    {
        EnsureOpen();
        if (!CanGoBack) throw new InvalidOperationException("The first step has no previous one.");
        Current = Steps[CurrentIndex - 1];
        return Current;
    }

    public void Finish()
    {
        EnsureOpen();
        if (!IsLastStep) throw new InvalidOperationException("Only the summary finishes the setup.");
        Outcome = GuidedSetupOutcome.Completed;
    }

    // "Not now", Esc or the window closed, at any step. Closing again
    // after Finish changes nothing.
    public void Dismiss()
    {
        if (Outcome == GuidedSetupOutcome.None) Outcome = GuidedSetupOutcome.Dismissed;
    }

    // Whether the window, once closed, has to set the device-local flag
    // that stops it from opening by itself: whichever way it closed.
    public bool MarksShown => Outcome != GuidedSetupOutcome.None;

    public EmailAccountState EmailAccount
        => SmtpConfigured ? EmailAccountState.Configured
            : IsAdministrator ? EmailAccountState.AdministratorCanSetUp
            : EmailAccountState.AdministratorRequired;

    // Read again when Settings, opened from the email step, closes.
    public void SetSmtpConfigured(bool configured) => SmtpConfigured = configured;

    // "For me": the user's address receives the warnings, the second
    // address is the person who assists them. "For someone I look
    // after": the user is the caregiver, the second address is the person
    // looked after.
    public GuidedSetupAddresses MapAddresses(string? userAddress, string? otherAddress)
    {
        var user = userAddress?.Trim() ?? string.Empty;
        var other = otherAddress?.Trim() ?? string.Empty;
        return Audience == GuidedSetupAudience.Myself
            ? new GuidedSetupAddresses(ToAddress: user, CaregiverAddress: other)
            : new GuidedSetupAddresses(ToAddress: other, CaregiverAddress: user);
    }

    // The two fields of the email step, filled from the saved settings.
    public (string User, string Other) AddressFields(GuidedSetupAddresses saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return Audience == GuidedSetupAudience.Myself
            ? (saved.ToAddress, saved.CaregiverAddress)
            : (saved.CaregiverAddress, saved.ToAddress);
    }

    // Email is the only channel and nothing can deliver it: no SMTP
    // account, or no address to send to. The summary then says these
    // medicines will warn no one and offers to add Windows.
    public bool WarnsNoOne(bool hasRecipient)
        => (_channels & NotificationChannels.Windows) == 0 && (!SmtpConfigured || !hasRecipient);

    // "Also warn on Windows" on the summary.
    public void AddWindowsChannel() => _channels |= NotificationChannels.Windows;

    // Whether the window opens by itself for a profile once its main
    // window is shown: only while the profile has no medicine at all
    // (inactive ones included) and this device has not shown it before.
    public static bool OpensByItself(bool profileHasMedicines, bool shownBefore)
        => !profileHasMedicines && !shownBefore;

    private void EnsureOpen()
    {
        if (Outcome != GuidedSetupOutcome.None) throw new InvalidOperationException("The guided setup is closed.");
    }
}
