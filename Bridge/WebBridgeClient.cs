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
    private readonly BridgeStartup _startup = new(BridgePolicy.StartTimeout, delay => Task.Delay(delay));
    private IntPtr _hostWindow;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private global::Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? _navigationCompleted;
    private global::Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>? _webMessageReceived;
    private global::Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs>? _processFailed;
    private string _startStage = "window";
    private bool _disposed;

    public WebBridgeClient()
    {
        _startup.Expired += Startup_Expired;
    }

    /// <summary>
    /// <see cref="BridgeConnectionState.Connected"/> means the bridge document loaded; it does not yet prove that the
    /// page script listens (a readiness handshake may upgrade this later).
    /// </summary>
    public BridgeConnectionState State { get; private set; } = BridgeConnectionState.NotStarted;

    /// <summary>Diagnostic for the last failure: the stage and an HRESULT or WebView2 error status. Never contains page data.</summary>
    public string? LastError { get; private set; }

    public event EventHandler? StateChanged;

    /// <summary>
    /// Starts the bridge; the task completes with true when the bridge document is loaded (Connected) and false when the
    /// start failed or passed <see cref="BridgePolicy.StartTimeout"/>. A start in progress or a live connection is shared
    /// by every caller; after a failure the next call tears down the failed controller and connects again, so a later
    /// request (for example a page's Retry) reconnects without restarting the app.
    /// </summary>
    public Task<bool> StartAsync()
    {
        var request = _startup.Request();
        if (request.IsNewAttempt)
        {
            Teardown(BridgeConnectionState.Connecting, "BRIDGE_UNAVAILABLE");
            _ = StartCoreAsync(request.Generation);
        }
        return request.Task;
    }

    private async Task StartCoreAsync(int generation)
    {
        _startStage = "window";
        try
        {
            if (_hostWindow == IntPtr.Zero)
                _hostWindow = CreateWindowExW(0, "Static", "MolHub data bridge", WS_POPUP, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            if (_hostWindow == IntPtr.Zero) throw new InvalidOperationException("Bridge host window unavailable.");
            _startStage = "environment";

            // Right after sign-in the sign-in WebView may still be shutting down the shared browser process,
            // so controller creation is retried with a short backoff before the bridge is reported unavailable.
            // The environment and the controller being created stay in locals; nothing reaches a field until the
            // generation is checked after each await, so a timed-out or superseded attempt changes nothing.
            CoreWebView2Controller controller;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var environment = await WebViewProfile.GetEnvironmentAsync();
                    if (!_startup.IsCurrent(generation)) return;
                    _startStage = "controller";
                    var created = await environment.CreateCoreWebView2ControllerAsync(
                        CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)_hostWindow));
                    if (!_startup.IsCurrent(generation))
                    {
                        CloseQuietly(created);
                        return;
                    }
                    controller = created;
                    break;
                }
                catch (Exception ex) when (attempt < 3 && _startup.IsCurrent(generation))
                {
                    LastError = $"{_startStage} 0x{ex.HResult:X8} (attempt {attempt})";
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                    if (!_startup.IsCurrent(generation)) return;
                    _startStage = "environment";
                }
            }
            _startStage = "settings";
            _controller = controller;
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
            // The stateful handlers capture this attempt's generation; senders are never compared by reference
            // (the projection does not guarantee the same wrapper object). Teardown unsubscribes exactly these delegates.
            _navigationCompleted = (_, e) => HandleNavigationCompleted(generation, e);
            _webMessageReceived = (_, e) => HandleWebMessage(generation, e);
            _processFailed = (_, e) => Fail(generation, $"process {e.ProcessFailedKind}");
            _core.NavigationCompleted += _navigationCompleted;
            _core.NewWindowRequested += Core_NewWindowRequested;
            _core.PermissionRequested += Core_PermissionRequested;
            _core.DownloadStarting += Core_DownloadStarting;
            _core.WebMessageReceived += _webMessageReceived;
            _core.ProcessFailed += _processFailed;
            _startStage = "navigate";
            _core.Navigate(BridgePolicy.BridgeUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Fail(generation, $"{_startStage} 0x{ex.HResult:X8}");
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

    private void HandleNavigationCompleted(int generation, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_disposed || !_startup.IsCurrent(generation) || State != BridgeConnectionState.Connecting) return;
        if (e.IsSuccess && BridgePolicy.IsBridgeDocument(TryParseUri(_core?.Source)))
        {
            if (_startup.Succeed(generation)) SetState(BridgeConnectionState.Connected);
        }
        else
        {
            Fail(generation, e.IsSuccess ? "navigation unexpected document" : $"navigation {e.WebErrorStatus} {e.HttpStatusCode}");
        }
    }

    private static void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) => e.Handled = true;

    private static void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
    }

    private static void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e) => e.Cancel = true;

    private void HandleWebMessage(int generation, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!_startup.IsLive(generation)) return;
        if (!BridgePolicy.IsTrustedSource(e.Source)) return;
        string json;
        try { json = e.WebMessageAsJson; }
        catch { return; }
        var result = BridgePolicy.ParseResult(json);
        if (result is null || !_pending.Remove(result.RequestId, out var completion)) return;
        completion.TrySetResult(result);
    }

    /// <summary>Fails the live generation (only if it still applies) and tears the connection down.</summary>
    private void Fail(int generation, string reason)
    {
        if (!_startup.Fail(generation)) return;
        LastError = reason;
        Teardown(BridgeConnectionState.Unavailable, "BRIDGE_UNAVAILABLE");
    }

    /// <summary>The deadline passed while the generation was still starting; it is already failed in the coordinator.</summary>
    private void Startup_Expired(int generation)
    {
        LastError = $"start timeout ({_startStage})";
        Teardown(BridgeConnectionState.Unavailable, "BRIDGE_UNAVAILABLE");
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
        _disposed = true;
        _startup.Dispose();
        Teardown(BridgeConnectionState.Closed, "BRIDGE_CLOSED");
        if (_hostWindow != IntPtr.Zero) DestroyWindow(_hostWindow);
        _hostWindow = IntPtr.Zero;
    }

    /// <summary>
    /// The single teardown for failure, deadline expiry, a new attempt and <see cref="Dispose"/>: unsubscribes every
    /// event, closes the controller, clears the fields, fails pending requests (writes stay Unknown) and sets the state.
    /// Idempotent; the host window stays for the next connection.
    /// </summary>
    private void Teardown(BridgeConnectionState state, string pendingCode)
    {
        if (_core is { } core)
        {
            core.NavigationStarting -= Core_NavigationStarting;
            core.FrameNavigationStarting -= Core_FrameNavigationStarting;
            if (_navigationCompleted is { } navigationCompleted) core.NavigationCompleted -= navigationCompleted;
            core.NewWindowRequested -= Core_NewWindowRequested;
            core.PermissionRequested -= Core_PermissionRequested;
            core.DownloadStarting -= Core_DownloadStarting;
            if (_webMessageReceived is { } webMessageReceived) core.WebMessageReceived -= webMessageReceived;
            if (_processFailed is { } processFailed) core.ProcessFailed -= processFailed;
        }
        _navigationCompleted = null;
        _webMessageReceived = null;
        _processFailed = null;
        var controller = _controller;
        _core = null;
        _controller = null;
        CloseQuietly(controller);
        FailPending(pendingCode);
        SetState(_disposed ? BridgeConnectionState.Closed : state);
    }

    private static void CloseQuietly(CoreWebView2Controller? controller)
    {
        try { controller?.Close(); } catch { /* The browser process may already be gone. */ }
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
