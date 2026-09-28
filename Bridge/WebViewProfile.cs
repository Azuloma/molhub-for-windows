using Microsoft.Web.WebView2.Core;
using Windows.Storage;

namespace MolHub.Windows;

/// <summary>
/// The single persistent WebView2 environment shared by the sign-in window and the hidden data bridge, so the
/// browser engine shares the HttpOnly session cookie without the host ever reading it.
/// </summary>
internal static class WebViewProfile
{
    public const string FolderName = "MolHub.WebView2";

    private static Task<CoreWebView2Environment>? _environment;

    /// <summary>Must be called on the UI thread. A failed creation is retried on the next call.</summary>
    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment is null || _environment.IsFaulted || _environment.IsCanceled) _environment = CreateAsync();
        return _environment;
    }

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        var profilePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, FolderName);
        Directory.CreateDirectory(profilePath);
        return await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: profilePath,
            options: new CoreWebView2EnvironmentOptions());
    }
}
