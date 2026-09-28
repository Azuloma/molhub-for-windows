using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MolHub.Windows;

/// <summary>
/// Building blocks for code-built data pages. Brushes come only from the `Dashboard*`/`Project*` styles in App.xaml,
/// so every part follows the RootGrid theme (Light, Dark or High Contrast).
/// </summary>
internal sealed class PageParts(Func<string, string> localize)
{
    public string L(string key) => localize(key);

    public static Style Res(string key) => (Style)Application.Current.Resources[key];

    public static FontIcon Glyph(string glyph, double size)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = size, Style = Res("DashboardIconStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        return icon;
    }

    public static Border Divider() => new() { Style = Res("DashboardDividerStyle") };

    /// <summary>
    /// Vertical page scroller around a column of at most <paramref name="maxWidth"/> DIP, centered in wide windows. The
    /// column is placed by <see cref="CenteredColumnPanel"/> instead of Stretch + MaxWidth: with that combination WinUI
    /// placed the column as if it were only as wide as its content, so a page with only short text (Profile settings)
    /// was pushed right and clipped.
    /// </summary>
    public static ScrollViewer CenteredPage(FrameworkElement column, double maxWidth)
    {
        var host = new CenteredColumnPanel { ColumnMaxWidth = maxWidth };
        host.Children.Add(column);
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = host
        };
    }

    public static TextBlock Heading(string text, AutomationHeadingLevel level, string style = "BodyStrongTextBlockStyle")
    {
        var heading = new TextBlock { Text = text, Style = Res(style), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetHeadingLevel(heading, level);
        return heading;
    }

    /// <summary>Card with a glyph + BodyStrong header, a divider and the body; an optional action sits on the header's right.</summary>
    public Border Card(string titleKey, string glyph, FrameworkElement body, FrameworkElement? action = null)
    {
        var header = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Glyph(glyph, 16));
        var title = Heading(L(titleKey), AutomationHeadingLevel.Level2);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        if (action is not null)
        {
            Grid.SetColumn(action, 2);
            header.Children.Add(action);
        }

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(Divider());
        panel.Children.Add(body);
        return new Border { Style = Res("DashboardCardStyle"), Child = panel };
    }

    public HyperlinkButton LinkButton(string text, Action action, Thickness margin, string? glyph = "")
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new TextBlock { Text = text });
        if (glyph is not null) content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, FontFamily = SymbolFont });
        var button = new HyperlinkButton { Content = content, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    public Button SubtleButton(string text, string glyph, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, FontFamily = SymbolFont });
        content.Children.Add(new TextBlock { Text = text });
        var button = new Button { Content = content, Style = (Style)Application.Current.Resources["SubtleButtonStyle"], VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    public TextBlock Secondary(string text, bool wrap = true) =>
        new() { Text = text, Style = Res("DashboardSecondaryTextStyle"), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };

    public static TextBlock Caption(string text) =>
        new() { Text = text, Style = Res("DashboardCaptionTextStyle"), TextWrapping = TextWrapping.Wrap };

    public static Border VersionBadge(string label) => new()
    {
        Style = Res("DashboardVersionBadgeStyle"),
        Child = new TextBlock { Text = label, Style = Res("DashboardVersionTextStyle") }
    };

    public Border PrivateBadge() => new()
    {
        Style = Res("ProjectPrivateBadgeStyle"),
        Child = new TextBlock { Text = L("Projects_Private"), Style = Res("DashboardCaptionTextStyle") }
    };

    /// <summary>The web's external-storage privacy note; shown wherever share links appear.</summary>
    public Border StorageNote()
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Glyph("", 14);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        row.Children.Add(icon);
        var text = Caption(L("Dashboard_StorageNote"));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return new Border { Style = Res("DashboardCardStyle"), Child = row };
    }

    /// <summary>Monospace 7-character ID with a copy button that copies the full ID and announces completion.</summary>
    public Border IdChip(string id, string copyNameFormat, string copiedKey)
    {
        var shortId = DashboardModel.ShortId(id);
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        chip.Children.Add(new TextBlock { Text = shortId, Style = Res("DashboardIdTextStyle") });
        var icon = new FontIcon { Glyph = "", FontSize = 12, FontFamily = SymbolFont };
        var copy = new Button { Content = icon, Style = (Style)Application.Current.Resources["SubtleButtonStyle"], Padding = new Thickness(6, 4, 6, 4), MinWidth = 0, MinHeight = 0 };
        var copyName = string.Format(copyNameFormat, shortId);
        AutomationProperties.SetName(copy, copyName);
        ToolTipService.SetToolTip(copy, copyName);
        copy.Click += async (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(id);
            Clipboard.SetContent(package);
            icon.Glyph = "";
            ToolTipService.SetToolTip(copy, L(copiedKey));
            if (FrameworkElementAutomationPeer.FromElement(copy) is { } peer)
            {
                peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, L(copiedKey), "PageCopy");
            }
            await Task.Delay(1500);
            icon.Glyph = "";
            ToolTipService.SetToolTip(copy, copyName);
        };
        chip.Children.Add(copy);
        return new Border { Style = Res("DashboardIdChipStyle"), Child = chip };
    }

    public static PersonPicture Avatar(string name, byte[]? png, double size)
    {
        var picture = new PersonPicture { Width = size, Height = size, DisplayName = name, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAccessibilityView(picture, AccessibilityView.Raw);
        if (png is { } avatar) AvatarImage.Attach(picture, avatar, (int)(size * 2));
        return picture;
    }

    public FrameworkElement LoadingIndicator(string key)
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 48, 0, 0) };
        panel.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32 });
        panel.Children.Add(new TextBlock { Text = L(key), Style = Res("DashboardSecondaryTextStyle") });
        AutomationProperties.SetName(panel, L(key));
        return panel;
    }

    public static Microsoft.UI.Xaml.Media.FontFamily SymbolFont =>
        (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"];
}

/// <summary>
/// Gives its child the available width (the scroll viewport, so a scroll bar that takes space is left out) up to
/// <see cref="ColumnMaxWidth"/> and centers it; the width is set during layout, so there is no first frame at another width.
/// </summary>
internal sealed class CenteredColumnPanel : Panel
{
    public double ColumnMaxWidth { get; set; } = double.PositiveInfinity;

    private double ColumnWidth(double available) => double.IsInfinity(available) ? ColumnMaxWidth : Math.Min(ColumnMaxWidth, available);

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size availableSize)
    {
        var width = ColumnWidth(availableSize.Width);
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new global::Windows.Foundation.Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new global::Windows.Foundation.Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize)
    {
        var width = ColumnWidth(finalSize.Width);
        foreach (var child in Children)
            child.Arrange(new global::Windows.Foundation.Rect((finalSize.Width - width) / 2, 0, width, finalSize.Height));
        return finalSize;
    }
}

/// <summary>
/// Lays children left to right and moves a child to the next line when it does not fit, so a long project name
/// wraps and its label follows instead of being clipped (a horizontal StackPanel never wraps).
/// </summary>
internal sealed class InlineWrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 10;

    public double VerticalSpacing { get; set; } = 4;

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size availableSize)
    {
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            var size = child.DesiredSize;
            var needed = lineWidth > 0 ? lineWidth + HorizontalSpacing + size.Width : size.Width;
            if (lineWidth > 0 && needed > availableSize.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + VerticalSpacing;
                lineWidth = size.Width;
                lineHeight = size.Height;
            }
            else
            {
                lineWidth = needed;
                lineHeight = Math.Max(lineHeight, size.Height);
            }
        }
        return new global::Windows.Foundation.Size(Math.Max(width, lineWidth), height + lineHeight);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        var line = new List<UIElement>();
        void FinishLine()
        {
            // Children on one line are centered vertically against the tallest one.
            double offset = 0;
            foreach (var item in line)
            {
                var size = item.DesiredSize;
                item.Arrange(new global::Windows.Foundation.Rect(offset, y + (lineHeight - size.Height) / 2, Math.Min(size.Width, finalSize.Width), size.Height));
                offset += size.Width + HorizontalSpacing;
            }
            line.Clear();
        }
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + HorizontalSpacing + size.Width > finalSize.Width)
            {
                FinishLine();
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            x = x > 0 ? x + HorizontalSpacing + size.Width : size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            line.Add(child);
        }
        FinishLine();
        return finalSize;
    }
}

/// <summary>A side column next to the main column when wide, stacked (in a chosen order) when narrow.</summary>
internal sealed class TwoColumnLayout
{
    public const double WideMinWidth = 860;

    private readonly FrameworkElement _main;
    private readonly FrameworkElement _side;
    private readonly double _sideWidth;
    private readonly bool _sideOnLeft;
    private readonly bool _sideFirstWhenStacked;

    public TwoColumnLayout(FrameworkElement main, FrameworkElement side, double sideWidth, bool sideOnLeft, bool sideFirstWhenStacked)
    {
        _main = main;
        _side = side;
        _sideWidth = sideWidth;
        _sideOnLeft = sideOnLeft;
        _sideFirstWhenStacked = sideFirstWhenStacked;
        Element.Children.Add(main);
        Element.Children.Add(side);
    }

    public Grid Element { get; } = new() { ColumnSpacing = 24, RowSpacing = 24 };

    public void Apply(double width)
    {
        if (width <= 0) return;
        Element.ColumnDefinitions.Clear();
        Element.RowDefinitions.Clear();
        if (width >= WideMinWidth)
        {
            var sideColumn = new ColumnDefinition { Width = new GridLength(_sideWidth) };
            var mainColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            Element.ColumnDefinitions.Add(_sideOnLeft ? sideColumn : mainColumn);
            Element.ColumnDefinitions.Add(_sideOnLeft ? mainColumn : sideColumn);
            Grid.SetRow(_main, 0);
            Grid.SetRow(_side, 0);
            Grid.SetColumn(_side, _sideOnLeft ? 0 : 1);
            Grid.SetColumn(_main, _sideOnLeft ? 1 : 0);
        }
        else
        {
            Element.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Element.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_main, 0);
            Grid.SetColumn(_side, 0);
            Grid.SetRow(_side, _sideFirstWhenStacked ? 0 : 1);
            Grid.SetRow(_main, _sideFirstWhenStacked ? 1 : 0);
        }
    }
}
