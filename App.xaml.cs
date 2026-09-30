using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Windows.Globalization;
using Windows.Storage;

namespace MolHub.Windows;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public static Window? LoginWindow { get; private set; }
    private bool _transitioning;
    private bool _signingOut;
    private Microsoft.UI.Dispatching.DispatcherQueue? _uiQueue;

    public App()
    {
        ApplySavedLanguage();
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        RegisterNotifications();
        // A launch from a toast (ExtendedActivationKind.AppNotification) starts normally through sign-in; its arguments are ignored.
        ShowLoginWindow();
    }

    private void RegisterNotifications()
    {
        try
        {
            _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += Notification_Invoked;
            manager.Register();
            NotificationToasts.Registered = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => UnregisterNotifications();
        }
        catch
        {
            // Toasts stay disabled; the app works without them.
            NotificationToasts.Registered = false;
        }
    }

    private void UnregisterNotifications()
    {
        if (!NotificationToasts.Registered) return;
        NotificationToasts.Registered = false;
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= Notification_Invoked;
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
            // Nothing to unregister.
        }
    }

    // Arrives off the UI thread: validate, then hand a target to MainWindow on the UI thread.
    private void Notification_Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var target = NotificationToastPolicy.ParseArguments(args.Arguments);
        if (target is null) return;
        _uiQueue?.TryEnqueue(() =>
        {
            if (_signingOut || MainWindow is not MolHub.Windows.MainWindow main) return;
            main.HandleToastActivation(target);
        });
    }

    private static void ApplySavedLanguage()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            var saved = values[SettingsPolicy.LanguageKey] as string;
            ApplicationLanguages.PrimaryLanguageOverride = SettingsPolicy.NormalizeLanguage(saved);
        }
        catch
        {
            // A settings store failure must not prevent sign-in. English is the safe fallback.
            try { ApplicationLanguages.PrimaryLanguageOverride = SettingsPolicy.DefaultLanguage; }
            catch { /* Keep the OS language if the host does not permit an override. */ }
        }
    }

    private LoginWindow ShowLoginWindow(bool resetSession = false)
    {
        var login = new LoginWindow(resetSession);
        LoginWindow = login;
        login.Authenticated += Login_Authenticated;
        login.SessionResetSucceeded += Login_SessionResetSucceeded;
        login.Closed += Login_Closed;
        login.Activate();
        return login;
    }

    private void Login_Authenticated(object? sender, AuthenticatedUser user)
    {
        if (_transitioning || (_signingOut && MainWindow is not null)) return;
        _transitioning = true;

        // Create and activate the authenticated window before closing the last login window.
        var authenticatedWindow = new MainWindow(user);
        MainWindow = authenticatedWindow;
        authenticatedWindow.Activate();

        var login = LoginWindow;
        LoginWindow = null;
        login?.Close();
        _signingOut = false;
        _transitioning = false;
    }

    private void Login_SessionResetSucceeded(object? sender, EventArgs args)
    {
        if (!_signingOut || MainWindow is null) return;

        // The replacement login window is already visible and has cleared its profile.
        // Only now remove the authenticated native window; there is never a zero-window gap.
        var main = MainWindow;
        MainWindow = null;
        main.Close();
        _signingOut = false;
    }

    private void Login_Closed(object sender, WindowEventArgs args)
    {
        if (_signingOut && MainWindow is not null)
        {
            _signingOut = false;
            // The window stays open, so the write gate that the sign-out closed accepts writes again.
            if (MainWindow is MolHub.Windows.MainWindow main) main.SignOutAbandoned();
            return;
        }

        if (!_transitioning && MainWindow is null)
        {
            UnregisterNotifications();
            Exit();
        }
    }

    public static void RequestSignOut()
    {
        var app = (App)Current;
        if (app._signingOut || MainWindow is null) return;
        app._signingOut = true;
        // Keep MainWindow alive while the replacement LoginWindow initializes and clears
        // the same WebView2 profile. LoginWindow navigates only after that clear succeeds.
        app.ShowLoginWindow(resetSession: true);
    }
}
