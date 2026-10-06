namespace FloatScreen;

internal static class WebAddress
{
    public static bool TryParse(string? input, out Uri? address)
    {
        address = null;
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace))
            return false;

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(parsed.Host)
            || Uri.CheckHostName(parsed.Host) == UriHostNameType.Unknown
            || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        address = parsed;
        return true;
    }
}
