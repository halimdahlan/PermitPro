#nullable disable

using System.Net;
using System.Text.Json.Serialization;

namespace PermitPro.App.Services;

// The site is proxied through Cloudflare, so every request arrives from a Cloudflare edge IP.
// CloudflareClientIpMiddleware restores the real client IP from the CF-Connecting-IP header —
// but only when the connection really comes from Cloudflare: the origin IP is public, and a client
// connecting directly could otherwise spoof the header (e.g. to dodge the per-IP rate limiter).
//
// The trusted ranges start from a built-in list and are refreshed from Cloudflare's API at startup
// and every 24 hours, so a change on Cloudflare's side needs no code change or redeploy.


/// <summary>Current set of Cloudflare edge networks whose CF-Connecting-IP header is trusted.</summary>
public class CloudflareIpRanges
{
	// Fallback until the first successful refresh, and whenever Cloudflare's API is unreachable.
	// Source: https://api.cloudflare.com/client/v4/ips (fetched 2026-10-07). A stale list fails safe:
	// requests from an unknown range are simply not trusted, never wrongly trusted.
	private static readonly string[] BuiltInNetworks =
	[
		"173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
		"141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
		"197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13",
		"104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
		"2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32",
		"2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32",
	];

	// Swapped as a whole (never mutated), so readers on other threads always see a complete list.
	private volatile IReadOnlyList<IPNetwork> _networks = Parse(BuiltInNetworks);

	public int Count => _networks.Count;

	public bool IsTrusted(IPAddress address)
	{
		if (address == null) return false;
		if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

		// Requests made on the server itself (and local development).
		if (IPAddress.IsLoopback(address)) return true;

		foreach (var network in _networks)
		{
			if (network.Contains(address)) return true;
		}

		return false;
	}

	public void Replace(IReadOnlyList<IPNetwork> networks) => _networks = networks;

	public static IReadOnlyList<IPNetwork> Parse(IEnumerable<string> cidrs)
		=> cidrs.Select(IPNetwork.Parse).ToList();
}


/// <summary>Refreshes <see cref="CloudflareIpRanges"/> from Cloudflare's public API.</summary>
public class CloudflareIpRangesRefresher : BackgroundService
{
	private const string ApiUrl = "https://api.cloudflare.com/client/v4/ips";
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
	private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

	private readonly CloudflareIpRanges _ranges;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger<CloudflareIpRangesRefresher> _logger;

	public CloudflareIpRangesRefresher(
		CloudflareIpRanges ranges,
		IHttpClientFactory httpClientFactory,
		ILogger<CloudflareIpRangesRefresher> logger)
	{
		_ranges = ranges;
		_httpClientFactory = httpClientFactory;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				var refreshed = await TryRefreshAsync(stoppingToken);
				await Task.Delay(refreshed ? RefreshInterval : RetryInterval, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// App shutting down.
		}
	}

	private async Task<bool> TryRefreshAsync(CancellationToken ct)
	{
		try
		{
			var client = _httpClientFactory.CreateClient(nameof(CloudflareIpRangesRefresher));
			var response = await client.GetFromJsonAsync<CloudflareIpsResponse>(ApiUrl, ct);

			var ipv4 = response?.Result?.Ipv4Cidrs;
			var ipv6 = response?.Result?.Ipv6Cidrs;

			// Never replace a working list with an empty or failed response.
			if (response?.Success != true || ipv4 is not { Count: > 0 } || ipv6 is not { Count: > 0 })
			{
				_logger.LogWarning("Cloudflare IP ranges response was unsuccessful or empty; keeping the current {Count} networks.", _ranges.Count);
				return false;
			}

			// Parse throws on a malformed CIDR, which also leaves the current list in place.
			_ranges.Replace(CloudflareIpRanges.Parse(ipv4.Concat(ipv6)));

			_logger.LogInformation("Cloudflare IP ranges refreshed: {IPv4} IPv4 and {IPv6} IPv6 networks.", ipv4.Count, ipv6.Count);
			return true;
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			_logger.LogWarning(ex, "Could not refresh Cloudflare IP ranges; keeping the current {Count} networks.", _ranges.Count);
			return false;
		}
	}

	private class CloudflareIpsResponse
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("result")]
		public CloudflareIpsResult Result { get; set; }
	}

	private class CloudflareIpsResult
	{
		[JsonPropertyName("ipv4_cidrs")]
		public List<string> Ipv4Cidrs { get; set; }

		[JsonPropertyName("ipv6_cidrs")]
		public List<string> Ipv6Cidrs { get; set; }
	}
}


/// <summary>Replaces the connection's remote IP with CF-Connecting-IP for requests from Cloudflare.</summary>
public class CloudflareClientIpMiddleware
{
	private readonly RequestDelegate _next;
	private readonly CloudflareIpRanges _ranges;

	public CloudflareClientIpMiddleware(RequestDelegate next, CloudflareIpRanges ranges)
	{
		_next = next;
		_ranges = ranges;
	}

	public Task InvokeAsync(HttpContext context)
	{
		// A repeated header joins with commas and fails to parse, so it is ignored too.
		if (_ranges.IsTrusted(context.Connection.RemoteIpAddress)
			&& IPAddress.TryParse(context.Request.Headers["CF-Connecting-IP"].ToString(), out var clientIp))
		{
			context.Connection.RemoteIpAddress = clientIp;
		}

		return _next(context);
	}
}
