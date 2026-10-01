using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MolHub.Windows;

/// <summary>
/// Sign-out sequence and write waiting for MainWindow: in-flight check, close the shared WriteGate, stop polling,
/// clear toasts, bridge "logout" when connected, then App.RequestSignOut().
/// </summary>
internal sealed class SignOutCoordinator
{
    private readonly WriteGate _writeGate;
    private readonly WebBridgeClient _bridge;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<string, string> _l;
    private readonly Func<XamlRoot?> _xamlRoot;
    private readonly Func<ElementTheme> _theme;
    private readonly Action _syncNotifications;
    private readonly Action _clearToasts;

    private bool _signingOut;
    // Set with _signingOut but kept until MainWindow closes or the sign-out is abandoned, so polling cannot restart meanwhile.
    private bool _signOutRequested;
    // A password change ended the session while another write was still being sent: sign out once the gate is free.
    private bool _signOutWhenGateFree;

    public SignOutCoordinator(
        WriteGate writeGate,
        WebBridgeClient bridge,
        DispatcherQueue dispatcher,
        Func<string, string> l,
        Func<XamlRoot?> xamlRoot,
        Func<ElementTheme> theme,
        Action syncNotifications,
        Action clearToasts)
    {
        _writeGate = writeGate;
        _bridge = bridge;
        _dispatcher = dispatcher;
        _l = l;
        _xamlRoot = xamlRoot;
        _theme = theme;
        _syncNotifications = syncNotifications;
        _clearToasts = clearToasts;
    }

    public bool IsSigningOut => _signingOut;
    public bool IsSignOutRequested => _signOutRequested;

    public async Task SignOutAsync()
    {
        if (_signingOut) return;
        // Signing out now would drop the answer of a write that is still being sent; sign out after it finishes.
        if (_writeGate.InFlight)
        {
            await ShowSignOutBlockedAsync();
            return;
        }
        _signingOut = true;
        _signOutRequested = true;
        // Same UI-thread turn as the check above: no new write may start while "logout" is awaited (a write that was refused
        // explains the sign-out). The gate stays closed until this window closes, or until an abandoned sign-out reopens it.
        _writeGate.Close();
        // Stop polling before "logout"; results that arrive later are ignored (generation check).
        _syncNotifications();
        _clearToasts();
        // Revoke the server session first when the bridge is up; local sign-in data is cleared either way.
        if (_bridge.State == BridgeConnectionState.Connected) await _bridge.RequestAsync("logout");
        App.RequestSignOut();
        _signingOut = false;
    }

    /// <summary>The sign-out was abandoned (its login window was closed) and this window stays open: writes may start again.</summary>
    public void SignOutAbandoned()
    {
        _writeGate.Reopen();
        _signOutRequested = false;
        _syncNotifications();
    }

    /// <summary>
    /// The session already ended on the server (a page's "Sign in again", or a password change that was applied or
    /// unconfirmed), so this sign-out is never refused: when another write is still being sent, it runs as soon as the
    /// write gate is released.
    /// </summary>
    public void SignOutWhenWritesFinish()
    {
        if (_writeGate.InFlight)
        {
            if (_signOutWhenGateFree) return;
            _signOutWhenGateFree = true;
            _writeGate.Released += SignOutWhenGateFree;
            return;
        }
        _ = SignOutAsync();
    }

    private void SignOutWhenGateFree()
    {
        if (_writeGate.InFlight) return;
        _writeGate.Released -= SignOutWhenGateFree;
        _signOutWhenGateFree = false;
        // Released is raised inside the finishing write's finally; sign out after it has returned, deferring again if
        // another write took the gate in between.
        if (!_dispatcher.TryEnqueue(SignOutWhenWritesFinish)) SignOutWhenWritesFinish();
    }

    private async Task ShowSignOutBlockedAsync()
    {
        var xamlRoot = _xamlRoot();
        if (xamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title = _l("Work_BusyTitle"),
            Content = new TextBlock { Text = _l("Shell_SignOutBlocked"), TextWrapping = TextWrapping.Wrap },
            CloseButtonText = _l("Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = _theme(),
            Style = PageParts.Res("DefaultContentDialogStyle")
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch
        {
            // Another dialog is already open; sign-out stays refused either way.
        }
    }
}
