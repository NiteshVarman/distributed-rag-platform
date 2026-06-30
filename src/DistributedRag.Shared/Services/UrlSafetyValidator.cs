using System.Net;
using System.Net.Sockets;

namespace DistributedRag.Shared.Services;

/// <summary>
/// SSRF guard for server-side URL fetching.
///
/// The scraper fetches caller-supplied URLs from inside the trusted network, so an
/// attacker could otherwise point it at cloud metadata endpoints (169.254.169.254),
/// loopback, or private RFC-1918 services and read internal responses through the
/// RAG answer. This validator resolves the host and rejects any URL that maps to a
/// non-public address, plus non-HTTP(S) schemes.
/// </summary>
public static class UrlSafetyValidator
{
    /// <summary>
    /// Returns true if the URL is safe to fetch from the server.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="reason">A human-readable reason when the URL is rejected.</param>
    public static async Task<bool> IsSafeAsync(string url, CancellationToken cancellationToken = default)
    {
        var (safe, _) = await ValidateAsync(url, cancellationToken);
        return safe;
    }

    /// <summary>
    /// Validates a URL for SSRF safety, returning a reason on failure.
    /// </summary>
    public static async Task<(bool Safe, string? Reason)> ValidateAsync(
        string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, "URL is not a valid absolute URI.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return (false, $"Unsupported scheme '{uri.Scheme}'. Only http/https are allowed.");

        // Resolve the host to its IP address(es). If ANY resolved address is
        // non-public, reject — this also blocks DNS names that point at internal IPs.
        IPAddress[] addresses;
        try
        {
            if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                && IPAddress.TryParse(uri.Host, out var literal))
            {
                addresses = [literal];
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Could not resolve host '{uri.Host}': {ex.Message}");
        }

        if (addresses.Length == 0)
            return (false, $"Host '{uri.Host}' did not resolve to any address.");

        foreach (var address in addresses)
        {
            if (IsPrivateOrReserved(address))
                return (false, $"Host '{uri.Host}' resolves to a private or reserved address ({address}).");
        }

        return (true, null);
    }

    /// <summary>
    /// Determines whether an IP address is in a private, loopback, link-local,
    /// or otherwise non-public range that must not be reachable from the scraper.
    /// </summary>
    private static bool IsPrivateOrReserved(IPAddress address)
    {
        // Normalize IPv4-mapped IPv6 (e.g. ::ffff:169.254.169.254) to IPv4.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))       // 127.0.0.0/8, ::1
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();

            return b[0] == 0                                   // 0.0.0.0/8 "this host"
                || b[0] == 10                                  // 10.0.0.0/8 private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64.0.0/10 CGNAT
                || (b[0] == 169 && b[1] == 254)                // 169.254.0.0/16 link-local (incl. cloud metadata)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 168)                // 192.168.0.0/16 private
                || b[0] >= 224;                                // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;

            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;                      // fc00::/7 unique local
        }

        // Unknown address family — reject by default.
        return true;
    }
}
