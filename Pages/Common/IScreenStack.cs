namespace MolHub.Windows;

/// <summary>A page with its own screens (list → item); MainWindow's Back walks these before leaving the page.</summary>
internal interface IScreenStack
{
    bool CanGoBack { get; }

    bool TryGoBack();
}
