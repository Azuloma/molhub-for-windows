namespace MolHub.Windows;

public static class NavigationPolicy
{
    public const string AppUrl = "https://fusion-ledger.desase0175.workers.dev/";

    private static readonly Uri AppOrigin = new(AppUrl);

    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(uri.Host, AppOrigin.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == AppOrigin.Port;
    }

    public static bool IsExternalLaunchable(Uri? uri)
    {
        if (uri is null || IsAllowed(uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }
}
