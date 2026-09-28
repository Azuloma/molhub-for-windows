using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.Resources;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace MolHub.Windows;

public sealed partial class LoginWindow : Window
{
    private readonly ProfileResponseCoordinator _responses = new();
    private readonly ResourceLoader _strings = ResourceLoader.GetForViewIndependentUse();
    private readonly ThemeService _themeService = new();
    private readonly bool _resetSessionBeforeNavigate;
    private CoreWebView2? _core;
    private bool _initialized;
    private bool _authenticationRaised;
    private bool _closed;

    public event EventHandler<AuthenticatedUser>? Authenticated;
    public event EventHandler? SessionResetSucceeded;

    public LoginWindow(bool resetSessionBeforeNavigate = false)
    {
        _resetSessionBeforeNavigate = resetSessionBeforeNavigate;
        InitializeComponent();
        _themeService.Apply(RootGrid, ThemeService.ReadSavedPreference());
        LoginWebView.Loaded += LoginWebView_Loaded;
        Closed += LoginWindow_Closed;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(LoginTitleBar);
        CaptionButtonTheme.Attach(this, RootGrid);
        WindowIcon.Apply(this);
        ConfigureCompactWindow();
        ApplyLocalizedStrings();
    }

    private string L(string key) => _strings.GetString(key);

    private void ConfigureCompactWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
            }

            var scale = GetDpiForWindow(hwnd) / 96.0;
            var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var client = LoginWindowLayoutPolicy.ClientSize(scale, work.Width, work.Height);
            appWindow.ResizeClient(new SizeInt32(client.Width, client.Height));
            // With content extended into the title bar, XAML also covers the caption area, but AppWindow's
            // client size excludes it. Remove the difference so the whole surface matches the layout height.
            if (GetClientRect(hwnd, out var rendered))
            {
                var extraHeight = rendered.Bottom - rendered.Top - client.Height;
                if (extraHeight > 0) appWindow.ResizeClient(new SizeInt32(client.Width, client.Height - extraHeight));
            }
            var outer = appWindow.Size;
            var position = LoginWindowLayoutPolicy.CenteredPosition(outer.Width, outer.Height, work.X, work.Y, work.Width, work.Height);
            appWindow.Move(new PointInt32(position.X, position.Y));
        }
        catch
        {
            // Keep the default window placement if the host does not expose AppWindow sizing.
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    private void ShowOverlay(string message, bool busy, bool recovery)
    {
        LoginStatus.Text = message;
        LoginProgress.IsActive = busy;
        LoginProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SessionRecoveryPanel.Visibility = recovery ? Visibility.Visible : Visibility.Collapsed;
        LoginOverlay.Visibility = Visibility.Visible;
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // Reveal the web sign-in surface once it has content, unless a recovery choice is pending.
        if (_closed || SessionRecoveryPanel.Visibility == Visibility.Visible) return;
        LoginProgress.IsActive = false;
        LoginOverlay.Visibility = Visibility.Collapsed;
    }

    private void ApplyLocalizedStrings()
    {
        Title = L("LoginTitle");
        AutomationProperties.SetName(LoginTitleBar, L("LoginTitle"));
        LoginStatus.Text = L("PleaseWait");
        AutomationProperties.SetName(LoginWebView, L("LoginWebViewName"));
        ResetSessionButton.Content = L("Retry");
        CloseSessionButton.Content = L("Close");
        AutomationProperties.SetName(ResetSessionButton, L("Retry"));
        AutomationProperties.SetName(CloseSessionButton, L("Close"));
    }

    private async void LoginWebView_Loaded(object sender, RoutedEventArgs e)
    {
        LoginWebView.Loaded -= LoginWebView_Loaded;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_initialized || _closed) return;
        try
        {
            var environment = await WebViewProfile.GetEnvironmentAsync();
            await LoginWebView.EnsureCoreWebView2Async(environment);
            _core = LoginWebView.CoreWebView2;
            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreHostObjectsAllowed = false;
            _core.Settings.IsWebMessageEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.NavigationStarting += Core_NavigationStarting;
            _core.NewWindowRequested += Core_NewWindowRequested;
            _core.PermissionRequested += Core_PermissionRequested;
            _core.WebResourceResponseReceived += Core_WebResourceResponseReceived;
            _core.NavigationCompleted += Core_NavigationCompleted;
            _initialized = true;

            if (_resetSessionBeforeNavigate)
            {
                await ResetSessionAndNavigateAsync();
            }
            else
            {
                _core.Navigate(NavigationPolicy.AppUrl);
            }
        }
        catch
        {
            ShowSessionRecoveryError();
        }
    }

    private async Task ResetSessionAndNavigateAsync()
    {
        if (_core is null || _closed) return;
        _responses.Invalidate();
        ShowOverlay(L("SigningOut"), busy: true, recovery: false);
        try
        {
            _core.Profile.CookieManager.DeleteAllCookies();
            await _core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
            if (_closed) return;
            SessionResetSucceeded?.Invoke(this, EventArgs.Empty);
            _core.Navigate(NavigationPolicy.AppUrl);
        }
        catch
        {
            ShowSessionRecoveryError();
        }
    }

    private void ShowSessionRecoveryError()
    {
        ShowOverlay(L("SessionClearError"), busy: false, recovery: true);
    }

    private async void ResetSessionButton_Click(object sender, RoutedEventArgs e)
    {
        await ResetSessionAndNavigateAsync();
    }

    private void CloseSessionButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = TryParseUri(e.Uri);
        if (NavigationPolicy.IsAllowed(uri)) return;
        e.Cancel = true;
        _ = OpenExternalAsync(uri);
    }

    private void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        var uri = TryParseUri(e.Uri);
        if (NavigationPolicy.IsAllowed(uri)) _core?.Navigate(uri!.AbsoluteUri);
        else _ = OpenExternalAsync(uri);
    }

    private static void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
    }

    private async void Core_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var uri = TryParseUri(e.Request.Uri);
        if (!ProfilePayloadParser.IsMeGet(uri, e.Request.Method)
            || e.Response.StatusCode is not (200 or 401) || _closed) return;

        var generation = _responses.BeginResponse();
        if (e.Response.StatusCode == 401)
        {
            _responses.Invalidate();
            LoginStatus.Text = L("SignInRequired");
            return;
        }

        try
        {
            using var content = await e.Response.GetContentAsync();
            using var stream = content.AsStream();
            var body = await ReadAtMostAsync(stream, ProfilePayloadParser.MaxResponseBytes);
            if (body is null || !_responses.IsCurrent(generation) || _closed) return;
            var user = ProfilePayloadParser.ParseAuthenticatedUser(e.Response.StatusCode, body);
            if (user is null || !_responses.IsCurrent(generation) || _closed)
            {
                LoginStatus.Text = L("SignInRequired");
                return;
            }

            if (!_authenticationRaised)
            {
                _authenticationRaised = true;
                Authenticated?.Invoke(this, user);
            }
        }
        catch
        {
            LoginStatus.Text = L("SignInRequired");
        }
    }

    private static async Task<byte[]?> ReadAtMostAsync(Stream stream, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static async Task<bool> OpenExternalAsync(Uri? uri)
    {
        if (!NavigationPolicy.IsExternalLaunchable(uri)) return false;
        try { return await Launcher.LaunchUriAsync(uri!); }
        catch { return false; }
    }

    private void LoginWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _responses.Invalidate();
        _themeService.Dispose();
        if (_core is not { } core) return;
        core.NavigationStarting -= Core_NavigationStarting;
        core.NewWindowRequested -= Core_NewWindowRequested;
        core.PermissionRequested -= Core_PermissionRequested;
        core.WebResourceResponseReceived -= Core_WebResourceResponseReceived;
        core.NavigationCompleted -= Core_NavigationCompleted;
    }

    private static Uri? TryParseUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
