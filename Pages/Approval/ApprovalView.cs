using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace FusionLedger.Windows;

/// <summary>
/// The web approval-waiting card for an account that is not approved yet: clock, amber "Awaiting approval" label,
/// heading, intro, Refresh and Announcements. Refresh re-reads the bridge `session`; when the same account is now
/// approved, MainWindow switches to the workspace without a new sign-in.
/// </summary>
internal sealed class ApprovalView : UserControl
{
    private const string ClockGlyph = "\uE823";
    private const string RefreshGlyph = "\uE72C";
    private const string HistoryGlyph = "\uE81C";

    private readonly PageParts _p;
    private readonly Func<Task<BridgeResult>> _checkSession;
    private readonly Action<AuthenticatedUser> _approved;
    private readonly Action<AuthenticatedUser> _updated;
    private readonly Action _signInAgain;
    private readonly InfoBar _statusBar = new() { IsClosable = true, IsOpen = false };
    private readonly Button _refresh;
    private AuthenticatedUser _user;

    public ApprovalView(Func<string, string> localize, AuthenticatedUser user, Func<Task<BridgeResult>> checkSession,
        Action<AuthenticatedUser> approved, Action<AuthenticatedUser> updated, Action openAnnouncements, Action signInAgain)
    {
        _p = new PageParts(localize);
        _user = user;
        _checkSession = checkSession;
        _approved = approved;
        _updated = updated;
        _signInAgain = signInAgain;

        var clock = PageParts.Glyph(ClockGlyph, 32);
        clock.HorizontalAlignment = HorizontalAlignment.Left;

        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new Ellipse { Width = 8, Height = 8, Style = PageParts.Res("ProjectStatusWorkingDotStyle"), VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(new TextBlock { Text = L("Page_Awaiting approval"), Style = PageParts.Res("ProjectStatusWorkingTextStyle") });

        _refresh = ActionButton(L("Dashboard_Refresh"), RefreshGlyph, accent: true);
        _refresh.Click += async (_, _) => await RefreshAsync();
        var announcements = ActionButton(L("Page_Announcements"), HistoryGlyph, accent: false);
        announcements.Click += (_, _) => openAnnouncements();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(_refresh);
        actions.Children.Add(announcements);

        var card = new StackPanel { Spacing = 12, Padding = new Thickness(32, 28, 32, 28) };
        card.Children.Add(clock);
        card.Children.Add(label);
        card.Children.Add(PageParts.Heading(L("Pending_Heading"), AutomationHeadingLevel.Level1, "SubtitleTextBlockStyle"));
        card.Children.Add(_p.Secondary(L("Pending_Intro")));
        card.Children.Add(actions);

        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32, 28, 32, 32), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new Border { Style = PageParts.Res("DashboardCardStyle"), Child = card });
        root.Children.Add(_statusBar);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
    }

    private string L(string key) => _p.L(key);

    private static Button ActionButton(string text, string glyph, bool accent)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, FontFamily = PageParts.SymbolFont });
        content.Children.Add(new TextBlock { Text = text });
        var button = new Button { Content = content };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        AutomationProperties.SetName(button, text);
        return button;
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        _statusBar.IsOpen = false;
        var (check, user) = ApprovalModel.Check(await _checkSession(), _user);
        _refresh.IsEnabled = true;
        if (check == ApprovalCheck.Approved && user is not null)
        {
            _approved(user);
            return;
        }
        if (user is not null)
        {
            // Still not approved, but the status may have changed (for example to rejected); keep the account menu current.
            _user = user;
            _updated(user);
        }

        var (severity, titleKey, messageKey) = check switch
        {
            ApprovalCheck.StillWaiting => (InfoBarSeverity.Informational, "Pending_StillWaitingTitle", "Pending_StillWaiting"),
            ApprovalCheck.SessionEnded => (InfoBarSeverity.Warning, "Dashboard_ErrorSessionTitle", "Pending_ErrorSession"),
            ApprovalCheck.Connection => (InfoBarSeverity.Error, "Dashboard_ErrorConnectionTitle", "Dashboard_ErrorConnection"),
            ApprovalCheck.RateLimited => (InfoBarSeverity.Warning, "Dashboard_ErrorRateLimitTitle", "Dashboard_ErrorRateLimit"),
            _ => (InfoBarSeverity.Error, "Pending_ErrorUnexpectedTitle", "Dashboard_ErrorUnexpected")
        };
        _statusBar.Severity = severity;
        _statusBar.Title = L(titleKey);
        _statusBar.Message = L(messageKey);
        if (check == ApprovalCheck.SessionEnded)
        {
            var signIn = new Button { Content = L("Dashboard_SignInAgain") };
            signIn.Click += (_, _) => _signInAgain();
            _statusBar.ActionButton = signIn;
        }
        else
        {
            _statusBar.ActionButton = null;
        }
        _statusBar.IsOpen = true;
        if (FrameworkElementAutomationPeer.FromElement(_statusBar) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
                _statusBar.Title, "ApprovalCheck");
        }
    }
}
