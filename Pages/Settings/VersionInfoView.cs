using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MolHub.Windows;

/// <summary>
/// The Version info page, built in code each time it is shown. The latest bridge status (key and detail) is kept here
/// and redrawn into whichever page was built last, so a rebuilt page shows the current status at once.
/// </summary>
internal sealed class VersionInfoView
{
    private readonly Func<string, string> _localize;
    private string _bridgeStatusKey = "BridgeConnecting";
    private string? _bridgeStatusDetail;
    private TextBlock? _bridgeStatusText;
    private TextBlock? _bridgeStatusDetailText;
    private Grid? _bridgeIndicatorHost;
    private StackPanel? _bridgeStatusPanel;

    public VersionInfoView(Func<string, string> localize)
    {
        _localize = localize;
    }

    private string L(string key) => _localize(key);

    public void SetBridgeStatus(string key, string? detail = null)
    {
        _bridgeStatusKey = key;
        _bridgeStatusDetail = detail;
        UpdateBridgeStatusView();
    }

    private static Style VersionStyle(string key) => (Style)Application.Current.Resources[key];

    private static FontIcon VersionGlyph(string glyph, string styleKey) =>
        new() { Glyph = glyph, FontFamily = PageParts.SymbolFont, Style = VersionStyle(styleKey) };

    /// <summary>Redraws the Version info status block (indicator, text, detail) from the bridge state; no-op until the page is built.</summary>
    private void UpdateBridgeStatusView()
    {
        if (_bridgeStatusPanel is null || _bridgeIndicatorHost is null || _bridgeStatusText is null || _bridgeStatusDetailText is null) return;

        string textStyle;
        string? detail = null;
        UIElement indicator;
        switch (_bridgeStatusKey)
        {
            case "BridgeConnected":
                textStyle = "VersionStatusSuccessTextStyle";
                detail = L("BridgeConnectedDetail");
                var disc = new Border { Style = VersionStyle("VersionStatusSuccessBadgeStyle") };
                disc.Child = VersionGlyph("\uE73E", "VersionStatusSuccessGlyphStyle");
                indicator = disc;
                break;
            case "BridgeSessionEnded":
                textStyle = "VersionStatusCautionTextStyle";
                detail = L("BridgeSessionEndedDetail");
                indicator = VersionGlyph("\uE7BA", "VersionStatusCautionGlyphStyle");
                break;
            case "BridgeUnavailable":
                textStyle = "VersionStatusCriticalTextStyle";
                detail = string.IsNullOrEmpty(_bridgeStatusDetail) ? null : _bridgeStatusDetail;
                indicator = VersionGlyph("\uE783", "VersionStatusCriticalGlyphStyle");
                break;
            default:
                textStyle = "VersionStatusTextStyle";
                indicator = new ProgressRing { Width = 20, Height = 20, IsActive = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                break;
        }

        _bridgeIndicatorHost.Children.Clear();
        _bridgeIndicatorHost.Children.Add(indicator);
        var status = L(_bridgeStatusKey);
        _bridgeStatusText.Text = status;
        _bridgeStatusText.Style = VersionStyle(textStyle);
        _bridgeStatusDetailText.Text = detail ?? string.Empty;
        _bridgeStatusDetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(_bridgeStatusPanel, string.IsNullOrEmpty(detail)
            ? $"{L("BridgeLabel")}: {status}"
            : $"{L("BridgeLabel")}: {status}, {detail}");
    }

    public FrameworkElement CreateVersionInfoPage()
    {
        var info = RuntimeVersionInfo.GetSnapshot();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(32) };

        // Hero card: banner logo, divider and the version column.
        var logo = new Image
        {
            Style = VersionStyle("VersionBannerImageStyle"),
            MaxHeight = 213,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(logo, L("VersionInfo_LogoName"));
        var heroDivider = new Border { Style = VersionStyle("VersionHeroDividerStyle"), Margin = new Thickness(16, 8, 16, 8) };
        var versionColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 300 };
        versionColumn.Children.Add(new TextBlock { Text = L("VersionLabel"), Style = VersionStyle("VersionHeroLabelStyle") });
        versionColumn.Children.Add(new TextBlock { Text = info.DisplayVersion, Style = VersionStyle("VersionHeroNumberStyle"), Margin = new Thickness(0, 0, 0, 16) });
        versionColumn.Children.Add(new TextBlock { Text = L("BetaLabel"), Style = VersionStyle("VersionHeroLabelStyle"), Margin = new Thickness(0, 0, 0, 8) });
        var badge = new Border { Style = VersionStyle("VersionChannelBadgeStyle"), Child = new TextBlock { Text = L("Beta"), Style = VersionStyle("VersionChannelBadgeTextStyle") } };
        versionColumn.Children.Add(badge);
        var hero = new Grid();
        hero.Children.Add(logo);
        hero.Children.Add(heroDivider);
        hero.Children.Add(versionColumn);
        panel.Children.Add(new Border { Style = VersionStyle("DashboardCardStyle"), Padding = new Thickness(16), Child = hero });

        // Lower row: runtime environment and build & connection cards.
        var runtimeRows = new StackPanel();
        AddVersionRow(runtimeRows, "\uE81E", "FrameworkLabel", info.Framework);
        AddVersionRow(runtimeRows, "\uF158", "WindowsAppSdkLabel", info.WindowsAppSdkVersion);
        AddVersionRow(runtimeRows, "\uE737", "WebViewLabel", info.WebViewVersion, L("VersionInfo_WebViewRole"));

        _bridgeIndicatorHost = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        _bridgeStatusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _bridgeStatusDetailText = new TextBlock { Style = VersionStyle("DashboardSecondaryTextStyle"), TextWrapping = TextWrapping.Wrap };
        var statusLine = new Grid { ColumnSpacing = 8 };
        statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_bridgeStatusText, 1);
        statusLine.Children.Add(_bridgeIndicatorHost);
        statusLine.Children.Add(_bridgeStatusText);
        _bridgeStatusPanel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        _bridgeStatusPanel.Children.Add(statusLine);
        _bridgeStatusPanel.Children.Add(_bridgeStatusDetailText);
        AutomationProperties.SetLiveSetting(_bridgeStatusPanel, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        var buildRows = new StackPanel();
        AddVersionRow(buildRows, "\uE950", "ArchitectureLabel", info.Architecture);
        AddVersionRow(buildRows, "\uF158", "PackageIdentityLabel", info.PackageIdentity);
        AddVersionRow(buildRows, "\uE753", "BridgeLabel", null, null, _bridgeStatusPanel);
        UpdateBridgeStatusView();

        var runtimeCard = CreateVersionCard(L("VersionInfo_RuntimeHeader"), runtimeRows);
        var buildCard = CreateVersionCard(L("VersionInfo_BuildHeader"), buildRows);
        var lower = new Grid();
        lower.Children.Add(runtimeCard);
        lower.Children.Add(buildCard);
        panel.Children.Add(lower);
        scroll.Content = panel;

        // Wide: hero in three columns and the cards side by side; narrow (< 860 available): everything stacks.
        bool? narrow = null;
        scroll.SizeChanged += (_, _) =>
        {
            var isNarrow = scroll.ActualWidth - 64 < 860;
            if (narrow == isNarrow) return;
            narrow = isNarrow;
            ApplyVersionLayout(isNarrow, hero, logo, heroDivider, versionColumn, lower, runtimeCard, buildCard);
        };
        ApplyVersionLayout(false, hero, logo, heroDivider, versionColumn, lower, runtimeCard, buildCard);
        return scroll;
    }

    private static void ApplyVersionLayout(bool narrow, Grid hero, FrameworkElement logo, FrameworkElement divider, FrameworkElement versionColumn,
        Grid lower, FrameworkElement runtimeCard, FrameworkElement buildCard)
    {
        hero.ColumnDefinitions.Clear();
        hero.RowDefinitions.Clear();
        lower.ColumnDefinitions.Clear();
        lower.RowDefinitions.Clear();
        if (narrow)
        {
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(logo, 0); Grid.SetRow(logo, 0);
            divider.Visibility = Visibility.Collapsed;
            Grid.SetColumn(divider, 0); Grid.SetRow(divider, 0);
            Grid.SetColumn(versionColumn, 0); Grid.SetRow(versionColumn, 1);
            versionColumn.Margin = new Thickness(0, 16, 0, 0);
            ((StackPanel)versionColumn).MinWidth = 0;

            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lower.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lower.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lower.ColumnSpacing = 0;
            lower.RowSpacing = 16;
            Grid.SetColumn(runtimeCard, 0); Grid.SetRow(runtimeCard, 0);
            Grid.SetColumn(buildCard, 0); Grid.SetRow(buildCard, 1);
            runtimeCard.VerticalAlignment = VerticalAlignment.Top;
            buildCard.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(logo, 0); Grid.SetRow(logo, 0);
            divider.Visibility = Visibility.Visible;
            Grid.SetColumn(divider, 1); Grid.SetRow(divider, 0);
            Grid.SetColumn(versionColumn, 2); Grid.SetRow(versionColumn, 0);
            versionColumn.Margin = new Thickness(40, 0, 0, 0);
            ((StackPanel)versionColumn).MinWidth = 300;

            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            lower.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lower.ColumnSpacing = 24;
            lower.RowSpacing = 0;
            Grid.SetColumn(runtimeCard, 0); Grid.SetRow(runtimeCard, 0);
            Grid.SetColumn(buildCard, 1); Grid.SetRow(buildCard, 0);
            runtimeCard.VerticalAlignment = VerticalAlignment.Stretch;
            buildCard.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private static Border CreateVersionCard(string header, StackPanel rows)
    {
        var headerText = new TextBlock
        {
            Text = header,
            Style = VersionStyle("SubtitleTextBlockStyle"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetHeadingLevel(headerText, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        var content = new StackPanel();
        content.Children.Add(headerText);
        content.Children.Add(rows);
        return new Border { Style = VersionStyle("DashboardCardStyle"), Padding = new Thickness(24, 20, 24, 20), Child = content };
    }

    /// <summary>Adds one icon + label + value row; a divider goes between rows, not after the last one.</summary>
    private void AddVersionRow(StackPanel rows, string glyph, string labelKey, string? value, string? note = null, UIElement? custom = null)
    {
        if (rows.Children.Count > 0) rows.Children.Add(new Border { Style = VersionStyle("DashboardDividerStyle") });
        var row = new Grid { Padding = new Thickness(0, 14, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(VersionGlyph(glyph, "VersionInfoRowIconStyle"));
        var text = new StackPanel { Spacing = 2 };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = L(labelKey), Style = VersionStyle("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
        if (value is not null) text.Children.Add(new TextBlock { Text = value, Style = VersionStyle("BodyTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
        if (note is not null) text.Children.Add(new TextBlock { Text = note, Style = VersionStyle("DashboardSecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        if (custom is not null) text.Children.Add(custom);
        row.Children.Add(text);
        rows.Children.Add(row);
    }
}
