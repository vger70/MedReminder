using Android.Runtime;

namespace MedReminder.MobileSpikes;

// Fully qualified: inside MedReminder.*, "Application" resolves to the
// MedReminder.Application namespace first.
[global::Android.App.Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
