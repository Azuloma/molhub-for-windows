using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FusionLedger.Windows;

/// <summary>
/// Native Profile settings for approved accounts, following the web profile screen: the identity (icon, username, role),
/// the icon (choose an image → preview → Save or Cancel; Remove icon after a confirmation) through `updateAvatar`, and
/// the password change through `changePassword`, which signs out every device. Each write goes through the app-wide
/// `WriteGate`, is sent once and is explained; an unknown outcome is never resent (the icon is re-read, an unconfirmed
/// password change ends with signing in again). Passwords are never logged or kept after sending.
/// </summary>
internal sealed class ProfileView : UserControl
{
    private const string RefreshGlyph = "\uE72C";
    private const string ProfileGlyph = "\uE77B";
    private const string PasswordGlyph = "\uE72E";
    private const int PictureSize = 96;
    private const int PictureDecodeWidth = 192;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private readonly PageParts _p;
    private readonly Func<string, JsonObject?, Task<BridgeResult>> _request;
    private readonly Func<AuthenticatedUser> _user;
    private readonly WriteGate _gate;
    private readonly Func<IntPtr> _windowHandle;
    private readonly Action<byte[]?> _avatarChanged;
    private readonly Action _signInAgain;
    private readonly Action _signOutAfterPasswordChange;

    private readonly InfoBar _statusBar = new() { IsClosable = true, IsOpen = false };
    private readonly ProgressBar _busy = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly Button _refresh;

    private readonly PersonPicture _identityPicture = new() { Width = PictureSize, Height = PictureSize, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _identityName;
    private readonly TextBlock _identityRole;
    private readonly FrameworkElement _identity;

    private readonly PersonPicture _preview = new() { Width = PictureSize, Height = PictureSize, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _previewCaption;
    private readonly Button _choose;
    private readonly Button _remove;
    private readonly Button _saveAvatar;
    private readonly Button _cancelAvatar;
    private readonly TextBlock _avatarError;

    private readonly PasswordBox _currentPassword;
    private readonly PasswordBox _newPassword;
    private readonly TextBlock _currentError;
    private readonly TextBlock _newError;
    private readonly Button _savePassword;
    private PasswordFieldError _shownPasswordErrors;

    private ProfileInfo? _profile;
    private byte[]? _pending;
    private bool _picking;
    // An icon write (and its reload) is running on this page.
    private bool _writing;
    private bool _loaded;
    private DateTimeOffset _loadedAt;
    private int _generation;

    public ProfileView(Func<string, string> localize, Func<string, JsonObject?, Task<BridgeResult>> request, Func<AuthenticatedUser> user,
        WriteGate gate, Func<IntPtr> windowHandle, Action<byte[]?> avatarChanged, Action signInAgain, Action signOutAfterPasswordChange)
    {
        _p = new PageParts(localize);
        _request = request;
        _user = user;
        _gate = gate;
        _windowHandle = windowHandle;
        _avatarChanged = avatarChanged;
        _signInAgain = signInAgain;
        _signOutAfterPasswordChange = signOutAfterPasswordChange;
        AutomationProperties.SetName(_busy, L("Work_Sending"));

        _refresh = _p.SubtleButton(L("Dashboard_Refresh"), RefreshGlyph, () => _ = LoadAsync());
        var title = new Grid { ColumnSpacing = 12 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(PageParts.Heading(L("Page_Profile settings"), AutomationHeadingLevel.Level1, "TitleTextBlockStyle"));
        Grid.SetColumn(_refresh, 1);
        title.Children.Add(_refresh);

        // Identity: icon, username and role, as at the top of the web profile screen.
        AutomationProperties.SetAccessibilityView(_identityPicture, AccessibilityView.Raw);
        _identityName = PageParts.Heading(string.Empty, AutomationHeadingLevel.None, "SubtitleTextBlockStyle");
        _identityRole = _p.Secondary(string.Empty);
        var identityText = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        identityText.Children.Add(_identityName);
        identityText.Children.Add(_identityRole);
        var identity = new Grid { ColumnSpacing = 16 };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identity.Children.Add(_identityPicture);
        Grid.SetColumn(identityText, 1);
        identity.Children.Add(identityText);
        _identity = identity;

        // Profile card: the icon with a preview that is the confirmation before saving.
        AutomationProperties.SetAccessibilityView(_preview, AccessibilityView.Raw);
        _previewCaption = PageParts.Caption(string.Empty);
        _choose = ActionButton(L("Profile_ChooseImage"));
        _choose.Click += (_, _) => _ = ChooseImageAsync();
        _remove = ActionButton(L("Profile_RemoveIcon"));
        _remove.Click += (_, _) => _ = RemoveIconAsync();
        _saveAvatar = ActionButton(L("Profile_Save"));
        _saveAvatar.Style = PageParts.Res("AccentButtonStyle");
        AutomationProperties.SetName(_saveAvatar, L("Profile_SaveIcon"));
        _saveAvatar.Click += (_, _) => { if (_pending is { } png) _ = SaveAvatarAsync(png); };
        _cancelAvatar = ActionButton(L("Work_CancelForm"));
        AutomationProperties.SetName(_cancelAvatar, L("Profile_CancelIcon"));
        _cancelAvatar.Click += (_, _) => ClearPending();
        _avatarError = FieldError();

        var avatarButtons = new InlineWrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        avatarButtons.Children.Add(_choose);
        avatarButtons.Children.Add(_remove);
        avatarButtons.Children.Add(_saveAvatar);
        avatarButtons.Children.Add(_cancelAvatar);
        var avatarSide = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        avatarSide.Children.Add(_previewCaption);
        avatarSide.Children.Add(avatarButtons);
        var avatarRow = new Grid { ColumnSpacing = 16 };
        avatarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        avatarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        avatarRow.Children.Add(_preview);
        Grid.SetColumn(avatarSide, 1);
        avatarRow.Children.Add(avatarSide);

        var profileBody = CardBody();
        profileBody.Children.Add(_p.Secondary(L("Profile_Intro")));
        profileBody.Children.Add(new TextBlock { Text = L("Profile_AvatarLabel"), Style = PageParts.Res("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
        profileBody.Children.Add(avatarRow);
        profileBody.Children.Add(_avatarError);

        // Change password card.
        _currentPassword = PasswordField(L("Profile_CurrentPassword"));
        _newPassword = PasswordField(L("Profile_NewPassword"));
        _currentError = FieldError();
        _newError = FieldError();
        _currentPassword.PasswordChanged += (_, _) => RecheckPasswords();
        _newPassword.PasswordChanged += (_, _) => RecheckPasswords();
        _savePassword = ActionButton(L("Profile_Save"));
        _savePassword.Style = PageParts.Res("AccentButtonStyle");
        AutomationProperties.SetName(_savePassword, L("Profile_SavePassword"));
        _savePassword.Click += (_, _) => _ = ChangePasswordAsync();
        var form = new StackPanel { Spacing = 8 };
        form.Children.Add(_currentPassword);
        form.Children.Add(_currentError);
        form.Children.Add(_newPassword);
        form.Children.Add(_newError);
        form.Children.Add(_savePassword);
        // The fields keep a readable width on wide windows and shrink with narrow ones.
        var formHost = new Grid();
        formHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 420 });
        formHost.Children.Add(form);

        var passwordBody = CardBody();
        passwordBody.Children.Add(_p.Secondary(L("Profile_PasswordHint")));
        passwordBody.Children.Add(formHost);

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(_identity);
        content.Children.Add(_p.Card("Profile_ProfileTitle", ProfileGlyph, profileBody));
        content.Children.Add(_p.Card("Profile_ChangePassword", PasswordGlyph, passwordBody));
        _content.Content = content;

        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32, 28, 32, 32) };
        root.Children.Add(title);
        root.Children.Add(_statusBar);
        root.Children.Add(_busy);
        root.Children.Add(_body);
        Content = PageParts.CenteredPage(root, 960);
        // Typed passwords do not stay in the cached page after leaving it.
        Unloaded += (_, _) => ClearPasswords();
        UpdateAvatarControls();
    }

    private string L(string key) => _p.L(key);

    /// <summary>Loads on first display and again when the data is older than a minute (a chosen, unsaved image stays).</summary>
    public Task EnsureLoadedAsync() =>
        !_loaded || DateTimeOffset.Now - _loadedAt > StaleAfter ? LoadAsync() : Task.CompletedTask;

    /// <summary>
    /// Reads `profile`; returns whether the profile is now shown (false when failed or superseded). While an icon write
    /// runs, only its own reload may read (a navigation or Retry load would supersede it and leave the result unexplained).
    /// </summary>
    private async Task<bool> LoadAsync(bool partOfWrite = false)
    {
        if (_writing && !partOfWrite) return false;
        var generation = ++_generation;
        if (!_loaded) _body.Content = _p.LoadingIndicator("Profile_Loading");
        _refresh.IsEnabled = false;
        var result = await _request("profile", null);
        if (generation != _generation) return false;
        // A reload that is part of a write keeps Refresh disabled until the write finishes.
        _refresh.IsEnabled = _busy.Visibility != Visibility.Visible;

        var profile = result.Ok && result.Data is { } data ? ProfileModel.Parse(data) : null;
        if (profile is null)
        {
            ShowError(result.Ok ? DashboardError.Unexpected : DashboardModel.ErrorFor(result));
            if (!_loaded) _body.Content = null;
            return false;
        }
        _statusBar.IsOpen = false;
        Apply(profile);
        _loaded = true;
        _loadedAt = DateTimeOffset.Now;
        _body.Content = _content;
        return true;
    }

    private void ShowError(DashboardError error)
    {
        var (severity, titleKey, messageKey) = error switch
        {
            DashboardError.Connection => (InfoBarSeverity.Error, "Dashboard_ErrorConnectionTitle", "Dashboard_ErrorConnection"),
            DashboardError.SessionEnded => (InfoBarSeverity.Warning, "Dashboard_ErrorSessionTitle", "Profile_ErrorSession"),
            DashboardError.PendingApproval => (InfoBarSeverity.Informational, "Dashboard_ErrorPendingTitle", "Dashboard_ErrorPending"),
            DashboardError.Maintenance => (InfoBarSeverity.Warning, "Dashboard_ErrorMaintenanceTitle", "Dashboard_ErrorMaintenance"),
            DashboardError.RateLimited => (InfoBarSeverity.Warning, "Dashboard_ErrorRateLimitTitle", "Dashboard_ErrorRateLimit"),
            _ => (InfoBarSeverity.Error, "Profile_ErrorUnexpectedTitle", "Dashboard_ErrorUnexpected")
        };
        _statusBar.Severity = severity;
        _statusBar.Title = L(titleKey);
        _statusBar.Message = L(messageKey);
        var action = new Button();
        if (error == DashboardError.SessionEnded)
        {
            action.Content = L("Dashboard_SignInAgain");
            action.Click += (_, _) => _signInAgain();
        }
        else
        {
            action.Content = L("Retry");
            action.Click += (_, _) => _ = LoadAsync();
        }
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
    }

    private void Apply(ProfileInfo profile)
    {
        _profile = profile;
        _identityPicture.DisplayName = profile.Username;
        _preview.DisplayName = profile.Username;
        _identityName.Text = profile.Username;
        var user = _user();
        _identityRole.Text = L(ProfileModel.RoleKey(user.Role, user.ProjectManager));
        AutomationProperties.SetName(_identity, $"{profile.Username}. {_identityRole.Text}");
        ShowPicture(_identityPicture, profile.Avatar);
        UpdateAvatarControls();
    }

    private static void ShowPicture(PersonPicture picture, byte[]? png)
    {
        if (png is { Length: > 0 }) AvatarImage.Attach(picture, png, PictureDecodeWidth);
        else AvatarImage.Clear(picture);
    }

    // ----- Icon -----

    /// <summary>Without a chosen image the card shows the saved icon; with one it shows the unsaved preview with Save / Cancel.</summary>
    private void UpdateAvatarControls()
    {
        var pending = _pending is not null;
        var saved = _profile?.Avatar is { Length: > 0 };
        ShowPicture(_preview, _pending ?? _profile?.Avatar);
        _previewCaption.Text = L(pending ? "Profile_PreviewCaption" : saved ? "Profile_CurrentIcon" : "Profile_NoIcon");
        _saveAvatar.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        _cancelAvatar.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        _remove.Visibility = !pending && saved ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ChooseImageAsync()
    {
        if (_picking) return;
        _picking = true;
        _choose.IsEnabled = false;
        ClearFieldError(_avatarError);
        try
        {
            StorageFile? file;
            try
            {
                file = await AvatarConverter.PickAsync(_windowHandle());
            }
            catch
            {
                file = null;
                ShowFieldError(_avatarError, L("Profile_PickerFailed"));
            }
            if (file is null) return;
            var png = await AvatarConverter.ConvertAsync(file);
            if (png is null)
            {
                ShowFieldError(_avatarError, L("Profile_ImageUnusable"));
                return;
            }
            _pending = png;
            UpdateAvatarControls();
            Announce(_previewCaption, _previewCaption.Text, "ProfilePreview");
        }
        finally
        {
            _picking = false;
            _choose.IsEnabled = true;
        }
    }

    /// <summary>Cancel: the chosen image is dropped and nothing is sent.</summary>
    private void ClearPending()
    {
        var hadFocus = _saveAvatar.FocusState != FocusState.Unfocused || _cancelAvatar.FocusState != FocusState.Unfocused;
        _pending = null;
        ClearFieldError(_avatarError);
        UpdateAvatarControls();
        if (hadFocus) _choose.Focus(FocusState.Programmatic);
    }

    private async Task RemoveIconAsync()
    {
        if (!await ConfirmAsync(L("Profile_RemoveConfirmTitle"), L("Profile_RemoveConfirm"), L("Profile_RemoveIcon"))) return;
        await SaveAvatarAsync(null);
    }

    /// <summary>
    /// Sends one `updateAvatar` (a PNG, or null to remove the icon). Applied: the returned profile is shown. Unknown: the
    /// profile is read again and shown, never resent. Either way MainWindow gets the resulting icon.
    /// </summary>
    private async Task SaveAvatarAsync(byte[]? png)
    {
        ClearFieldError(_avatarError);
        var payload = ProfileModel.AvatarPayload(png is null ? null : ProfileModel.ToDataUrl(png));
        if (payload is null)
        {
            ShowFieldError(_avatarError, L("Profile_ErrorInvalidAvatar"));
            return;
        }
        if (!_gate.TryEnter())
        {
            ShowNotice(InfoBarSeverity.Informational, L("Work_BusyTitle"), L("Work_Busy"));
            return;
        }
        SetBusy(true);
        _writing = true;
        // A read that started before this write must not show an older profile after it.
        _generation++;
        try
        {
            _statusBar.IsOpen = false;
            var result = await _request("updateAvatar", payload);
            var outcome = ProfileModel.Explain(result, ProfileWrite.Avatar);
            switch (outcome.Kind)
            {
                case WriteOutcomeKind.Applied:
                {
                    var updated = result.Data is { } data ? ProfileModel.Parse(data) : null;
                    var shown = true;
                    if (updated is not null)
                    {
                        Apply(updated);
                        _loadedAt = DateTimeOffset.Now;
                    }
                    else
                    {
                        shown = await LoadAsync(partOfWrite: true);
                    }
                    _pending = null;
                    UpdateAvatarControls();
                    // The server applied exactly what was sent, so without a readable profile the sent icon is the current one.
                    _avatarChanged(shown ? _profile?.Avatar : png);
                    ShowNotice(InfoBarSeverity.Success, L("Work_DoneTitle"), shown ? L("Profile_Saved") : L("Profile_Saved") + " " + L("Profile_NotRefreshed"));
                    break;
                }
                case WriteOutcomeKind.Unknown:
                {
                    var shown = await LoadAsync(partOfWrite: true);
                    // The chosen image is dropped either way, so Save is not offered as a one-click resend.
                    _pending = null;
                    UpdateAvatarControls();
                    if (shown) _avatarChanged(_profile?.Avatar);
                    ShowNotice(InfoBarSeverity.Warning, L(outcome.TitleKey), L(shown ? outcome.MessageKey : "Profile_AvatarUnknownNotRefreshed"));
                    break;
                }
                default:
                    ShowNotice(InfoBarSeverity.Error, L(outcome.TitleKey), L(outcome.MessageKey), outcome.SessionEnded);
                    break;
            }
        }
        finally
        {
            _writing = false;
            SetBusy(false);
            _gate.Exit();
        }
    }

    // ----- Password -----

    private static PasswordBox PasswordField(string header)
    {
        var box = new PasswordBox { Header = header, MaxLength = ProfileModel.MaxPasswordLength };
        AutomationProperties.SetName(box, header);
        return box;
    }

    /// <summary>A flagged field's check text disappears once that field is valid again.</summary>
    private void RecheckPasswords()
    {
        if (_shownPasswordErrors == PasswordFieldError.None) return;
        _shownPasswordErrors = ProfileModel.StillShown(_shownPasswordErrors,
            ProfileModel.ValidatePasswords(_currentPassword.Password, _newPassword.Password));
        if (!_shownPasswordErrors.HasFlag(PasswordFieldError.CurrentLength)) ClearFieldError(_currentError);
        if (!_shownPasswordErrors.HasFlag(PasswordFieldError.NewLength)) ClearFieldError(_newError);
    }

    private void ClearPasswords()
    {
        _currentPassword.Password = string.Empty;
        _newPassword.Password = string.Empty;
        _shownPasswordErrors = PasswordFieldError.None;
        ClearFieldError(_currentError);
        ClearFieldError(_newError);
    }

    /// <summary>
    /// Checks both fields, confirms that every device will be signed out, then sends one `changePassword`. The fields are
    /// cleared after sending. Applied or unknown: explained, then (after the write gate is released) the app signs out.
    /// </summary>
    private async Task ChangePasswordAsync()
    {
        var errors = ProfileModel.ValidatePasswords(_currentPassword.Password, _newPassword.Password);
        _shownPasswordErrors = errors;
        ClearFieldError(_currentError);
        ClearFieldError(_newError);
        if (errors.HasFlag(PasswordFieldError.CurrentLength)) ShowFieldError(_currentError, L("Profile_ErrorPasswordLength"));
        if (errors.HasFlag(PasswordFieldError.NewLength)) ShowFieldError(_newError, L("Profile_ErrorPasswordLength"));
        if (errors != PasswordFieldError.None) return;

        if (!await ConfirmAsync(L("Profile_PasswordConfirmTitle"), L("Profile_PasswordConfirm"), L("Profile_ChangePassword"))) return;
        // Read after the confirmation; the fields cannot change while the dialog is open.
        var payload = ProfileModel.PasswordPayload(_currentPassword.Password, _newPassword.Password);
        if (payload is null) return;
        if (!_gate.TryEnter())
        {
            ShowNotice(InfoBarSeverity.Informational, L("Work_BusyTitle"), L("Work_Busy"));
            return;
        }
        WriteOutcome outcome;
        SetBusy(true);
        try
        {
            _statusBar.IsOpen = false;
            var result = await _request("changePassword", payload);
            payload.Clear();
            ClearPasswords();
            outcome = ProfileModel.Explain(result, ProfileWrite.Password);
        }
        finally
        {
            SetBusy(false);
            _gate.Exit();
        }

        switch (outcome.Kind)
        {
            case WriteOutcomeKind.Applied:
                await ExplainAsync(L("Profile_PasswordChangedTitle"), L("Profile_PasswordChanged"));
                _signOutAfterPasswordChange();
                break;
            case WriteOutcomeKind.Unknown:
                await ExplainAsync(L(outcome.TitleKey), L(outcome.MessageKey));
                _signOutAfterPasswordChange();
                break;
            default:
                ShowNotice(InfoBarSeverity.Error, L(outcome.TitleKey), L(outcome.MessageKey), outcome.SessionEnded);
                break;
        }
    }

    // ----- Shared -----

    private static StackPanel CardBody() => new() { Spacing = 12, Padding = new Thickness(16, 12, 16, 16) };

    private static Button ActionButton(string text)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static TextBlock FieldError() => new() { Style = PageParts.Res("ManageFieldErrorTextStyle"), Visibility = Visibility.Collapsed };

    private static void ShowFieldError(TextBlock error, string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
        Announce(error, text, "ProfileFormCheck");
    }

    private static void ClearFieldError(TextBlock error)
    {
        error.Text = string.Empty;
        error.Visibility = Visibility.Collapsed;
    }

    private static void Announce(FrameworkElement element, string text, string activityId)
    {
        if ((FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element)) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, activityId);
        }
    }

    /// <summary>Removing the icon and changing the password are confirmed first ("Cancel" is the default).</summary>
    private async Task<bool> ConfirmAsync(string title, string text, string action)
    {
        if (_gate.InFlight)
        {
            ShowNotice(InfoBarSeverity.Informational, L("Work_BusyTitle"), L("Work_Busy"));
            return false;
        }
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = action,
            CloseButtonText = L("Work_CancelForm"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            // A code-built dialog needs the WinUI 3 style explicitly, or it gets the legacy template.
            Style = PageParts.Res("DefaultContentDialogStyle")
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            // Another dialog is already open; do nothing without a confirmation.
            return false;
        }
    }

    /// <summary>The explanation before signing out after a password change (its only button leads to the sign-in window).</summary>
    /// <remarks>Never throws: the caller signs out right after it, whether or not the dialog could be shown.</remarks>
    private async Task ExplainAsync(string title, string text)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = L("Dashboard_SignInAgain"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Style = PageParts.Res("DefaultContentDialogStyle")
            };
            await dialog.ShowAsync();
        }
        catch
        {
            // Another dialog is already open (or the page left the tree): the explanation is skipped, signing out still follows.
        }
    }

    private void SetBusy(bool value)
    {
        _busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        _content.IsEnabled = !value;
        _refresh.IsEnabled = !value;
    }

    private void ShowNotice(InfoBarSeverity severity, string title, string message, bool sessionEnded = false)
    {
        _statusBar.Severity = severity;
        _statusBar.Title = title;
        _statusBar.Message = message;
        Button? action = null;
        if (sessionEnded)
        {
            action = new Button { Content = L("Dashboard_SignInAgain") };
            action.Click += (_, _) => _signInAgain();
        }
        _statusBar.ActionButton = action;
        _statusBar.IsOpen = true;
        if (FrameworkElementAutomationPeer.FromElement(_statusBar) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
                $"{title}. {message}", "ProfileWrite");
        }
    }
}
