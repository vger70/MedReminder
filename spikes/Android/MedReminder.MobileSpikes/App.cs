namespace MedReminder.MobileSpikes;

// Fully qualified base type: inside MedReminder.*, "Application"
// resolves to the MedReminder.Application namespace first.
public sealed class App : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new NavigationPage(new MainPage()));
}
