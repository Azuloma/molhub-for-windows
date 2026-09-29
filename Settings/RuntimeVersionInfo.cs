using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace MolHub.Windows;

public sealed record RuntimeVersionSnapshot(
    string DisplayVersion,
    string Framework,
    string WindowsAppSdkVersion,
    string WebViewVersion,
    string Architecture,
    string PackageIdentity);

public static class RuntimeVersionInfo
{
    public static string GetDisplayVersion(Assembly? assembly = null)
    {
        var version = (assembly ?? typeof(App).Assembly).GetName().Version;
        return version is null ? "Unavailable" : $"v{version.ToString(3)}";
    }

    public static RuntimeVersionSnapshot GetSnapshot()
    {
        var packageIdentity = "Unavailable";
        try
        {
            var package = global::Windows.ApplicationModel.Package.Current;
            packageIdentity = $"{package.Id.Name} {package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision}";
        }
        catch
        {
            // Unpackaged development runs do not have a package identity.
        }

        var appSdkVersion = GetWindowsAppRuntimeVersion();
        var webViewVersion = "Unavailable";
        try
        {
            var available = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrWhiteSpace(available)) webViewVersion = available;
        }
        catch
        {
            // WebView2 may not be installed on a development machine.
        }

        return new RuntimeVersionSnapshot(
            GetDisplayVersion(),
            "WinUI 3",
            appSdkVersion,
            $"WebView2 {webViewVersion}",
            RuntimeInformation.ProcessArchitecture.ToString(),
            packageIdentity);
    }

    private static string GetWindowsAppRuntimeVersion()
    {
        try
        {
            var package = global::Windows.ApplicationModel.Package.Current;
            var runtimePackage = package.Dependencies.FirstOrDefault(dependency =>
                dependency.Id.Name.StartsWith("Microsoft.WindowsAppRuntime", StringComparison.OrdinalIgnoreCase));
            if (runtimePackage is null) return "Unavailable";

            var version = runtimePackage.Id.Version;
            return $"{runtimePackage.Id.Name} {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            // Unpackaged development runs or incomplete framework registration have no runtime package.
            return "Unavailable";
        }
    }
}
