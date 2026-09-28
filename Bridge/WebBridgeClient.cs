using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace MolHub.Windows;

public enum BridgeConnectionState
{
    NotStarted,
    Connecting,
    Connected,
    Unavailable,
    Closed
}

/// <summary>
/// Hosts the MolHub data bridge in a hidden WebView2 controller inside its own invisible window, separate from the
/// sign-in WebView (which keeps WebMessage disabled) and from MainWindow's XAML. Only the exact production
/// bridge document may load, only that document may answer, and only allow-listed commands are sent.
/// All members must be used on the UI thread.
/// </summary>
internal sealed class WebBridgeClient : IDisposable
{
    private readonly Dictionary<string, TaskCompletionSource<BridgeResult>> _pending = new(StringComparer.Ordinal);
    private TaskCompletionSource<bool>? _ready;
    private IntPtr _hostWindow;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private bool _disposed;

    public BridgeConnectionState State { get; private set; } = BridgeConnectionState.NotStarted;

    /// <summary>Diagnostic for the last failure: the stage and an HRESULT or WebView2 error status. Never contains page data.</summary>
    public string? LastError { get; private set; }

    public event EventHandler? StateChanged;

    /// <summary>
    /// Starts the bridge; the task completes with true when the bridge page is loaded and listening. A start in progress
    /// or a live connection is shared by every caller; after a failure the next call closes the failed controller and
    /// connects again, so a later request (for example a page's Retry) reconnects without restarting the app.
    /// </summary>
    public Task<bool> StartAsync()
    {
        if (_disposed) return Task.FromResult(false);
        if (_ready is null || State == BridgeConnectionState.Unavailable)
        {
            CloseController();
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = StartCoreAsync();
        }
        return _ready.Task;
    }

    private async Task StartCoreAsync()
    {
        SetState(BridgeConnectionState.Connecting);
        var stage = "window";
        try
        {
            if (_hostWindow == IntPtr.Zero)
                _hostWindow = CreateWindowExW(0, "Static", "MolHub data bridge", WS_POPUP, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            if (_hostWindow == IntPtr.Zero) throw new InvalidOperationException("Bridge host window unavailable.");

            // Right after sign-in the sign-in WebView may still be shutting down the shared browser process,
            // so controller creation is retried with a short backoff before the bridge is reported unavailable.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    stage = "environment";
                    var environment = await WebViewProfile.GetEnvironmentAsync();
                    if (_disposed) return;
                    stage = "controller";
                    _controller = await environment.CreateCoreWebView2ControllerAsync(
                        CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)_hostWindow));
                    break;
                }
                catch (Exception ex) when (attempt < 3 && !_disposed)
                {
                    LastError = $"{stage} 0x{ex.HResult:X8} (attempt {attempt})";
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                }
            }
            if (_disposed) { _controller.Close(); _controller = null; return; }
            stage = "settings";
            _controller.IsVisible = false;

            _core = _controller.CoreWebView2;
            var settings = _core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = true;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;

            _core.NavigationStarting += Core_NavigationStarting;
            _core.FrameNavigationStarting += Core_FrameNavigationStarting;
            _core.NavigationCompleted += Core_NavigationCompleted;
            _core.NewWindowRequested += Core_NewWindowRequested;
            _core.PermissionRequested += Core_PermissionRequested;
            _core.DownloadStarting += Core_DownloadStarting;
            _core.WebMessageReceived += Core_WebMessageReceived;
            _core.ProcessFailed += Core_ProcessFailed;
            stage = "navigate";
            _core.Navigate(BridgePolicy.BridgeUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Fail($"{stage} 0x{ex.HResult:X8}");
        }
    }

    /// <summary>
    /// Sends one allow-listed command. Never throws; failures come back as a status-0 result whose outcome says
    /// whether a write may have been applied (Unknown) or was never sent (Rejected).
    /// </summary>
    public async Task<BridgeResult> RequestAsync(string command, JsonObject? payload = null)
    {
        var requestId = BridgePolicy.NewRequestId();
        var write = BridgePolicy.IsWrite(command);
        var notSent = write ? BridgeOutcome.Rejected : BridgeOutcome.None;
        if (!BridgePolicy.IsKnownCommand(command))
            return BridgeResult.HostFailure(requestId, "UNSUPPORTED_OPERATION", false, notSent);
        var message = BridgePolicy.BuildRequest(command, requestId, payload);
        if (message is null) return BridgeResult.HostFailure(requestId, "TOO_LARGE", false, notSent);
        if (!await StartAsync() || _core is null || State != BridgeConnectionState.Connected)
            return BridgeResult.HostFailure(requestId, "BRIDGE_UNAVAILABLE", !write, notSent);

        var completion = new TaskCompletionSource<BridgeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;
        try
        {
            _core.PostWebMessageAsJson(message);
        }
        catch
        {
            _pending.Remove(requestId);
            return BridgeResult.HostFailure(requestId, "BRIDGE_UNAVAILABLE", !write, notSent);
        }

        var timeout = Task.Delay(write ? BridgePolicy.WriteTimeout : BridgePolicy.ReadTimeout);
        if (await Task.WhenAny(completion.Task, timeout) == completion.Task) return await completion.Task;
        _pending.Remove(requestId);
        return BridgeResult.HostFailure(requestId, "TIMEOUT", !write, write ? BridgeOutcome.Unknown : BridgeOutcome.None);
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (BridgePolicy.IsBridgeDocument(TryParseUri(e.Uri))) return;
        e.Cancel = true;
    }

    private static void Core_FrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) => e.Cancel = true;

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_disposed || State != BridgeConnectionState.Connecting) return;
        if (e.IsSuccess && BridgePolicy.IsBridgeDocument(TryParseUri(_core?.Source)))
        {
            SetState(BridgeConnectionState.Connected);
            _ready?.TrySetResult(true);
        }
        else
        {
            Fail(e.IsSuccess ? "navigation unexpected document" : $"navigation {e.WebErrorStatus} {e.HttpStatusCode}");
        }
    }

    private static void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) => e.Handled = true;

    private static void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
    }

    private static void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e) => e.Cancel = true;

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!BridgePolicy.IsTrustedSource(e.Source)) return;
        string json;
        try { json = e.WebMessageAsJson; }
        catch { return; }
        var result = BridgePolicy.ParseResult(json);
        if (result is null || !_pending.Remove(result.RequestId, out var completion)) return;
        completion.TrySetResult(result);
    }

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e) => Fail($"process {e.ProcessFailedKind}");

    private void Fail(string reason)
    {
        if (_disposed) return;
        LastError = reason;
        SetState(BridgeConnectionState.Unavailable);
        _ready?.TrySetResult(false);
        FailPending("BRIDGE_UNAVAILABLE");
    }

    private void FailPending(string code)
    {
        // A request already posted may have reached the server, so writes stay Unknown.
        foreach (var (requestId, completion) in _pending.ToArray())
        {
            completion.TrySetResult(BridgeResult.HostFailure(requestId, code, false, BridgeOutcome.Unknown));
        }
        _pending.Clear();
    }

    private void SetState(BridgeConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        FailPending("BRIDGE_CLOSED");
        _ready?.TrySetResult(false);
        SetState(BridgeConnectionState.Closed);
        _disposed = true;
        CloseController();
        if (_hostWindow != IntPtr.Zero) DestroyWindow(_hostWindow);
        _hostWindow = IntPtr.Zero;
    }

    /// <summary>Detaches and closes the current controller; the host window stays for the next connection.</summary>
    private void CloseController()
    {
        if (_core is { } core)
        {
            core.NavigationStarting -= Core_NavigationStarting;
            core.FrameNavigationStarting -= Core_FrameNavigationStarting;
            core.NavigationCompleted -= Core_NavigationCompleted;
            core.NewWindowRequested -= Core_NewWindowRequested;
            core.PermissionRequested -= Core_PermissionRequested;
            core.DownloadStarting -= Core_DownloadStarting;
            core.WebMessageReceived -= Core_WebMessageReceived;
            core.ProcessFailed -= Core_ProcessFailed;
        }
        _core = null;
        try { _controller?.Close(); } catch { /* The browser process may already be gone. */ }
        _controller = null;
    }

    private static Uri? TryParseUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private const uint WS_POPUP = 0x80000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
