namespace PermitPro.Core.Helpers;

public enum TenantHostKind
{
	Apex,       // permitpro.app (and www.): company address page, Super Admin
	Company,    // {subdomain}.permitpro.app
	Alias,      // a full host name mapped to one company (HostAliases)
	Other,      // anything else AllowedHosts lets through, e.g. localhost on the server: treated like the apex
}

// Path prefix for a company's pages: "" on the company's own host (iotm.permitpro.app/permits), "/{id}" where
// the host doesn't name the company (the apex, for a company without a subdomain).
public static class CompanyPaths
{
	// HttpContext.Items key, set to "" by the web app's tenant middleware on a company's host.
	public const string ItemKey = "PermitPro.CompanyPathBase";

	public static string Base(IDictionary<object, object?>? items, Guid companyId)
		=> items != null && items.TryGetValue(ItemKey, out var value) && value is string pathBase ? pathBase : $"/{companyId}";
}


// "Tenancy" section of appsettings.json: each company is served on {Subdomain}.{RootDomain}.
public class TenancySettings
{
	// permitpro.app in production; localhost in development (browsers send iotm.localhost to this machine).
	public string RootDomain { get; set; } = "permitpro.app";

	// Scheme and port of links built without a request (permit rule emails). Port null = the scheme's default.
	public string Scheme { get; set; } = "https";
	public int? Port { get; set; }

	// Full host names that each serve one company, e.g. { "demo.permitpro.app": "iotm" } for the demo site,
	// which has no subdomains of its own. Such a host also serves /admin, as there's no apex beside it.
	public Dictionary<string, string> HostAliases { get; set; } = new();

	// host: without the port (HostString.Host).
	public (TenantHostKind Kind, string? Subdomain) ParseHost(string host)
	{
		host = host.TrimEnd('.').ToLowerInvariant();

		foreach (var (aliasHost, subdomain) in HostAliases)
		{
			if (string.Equals(aliasHost, host, StringComparison.OrdinalIgnoreCase))
				return (TenantHostKind.Alias, subdomain.ToLowerInvariant());
		}

		var root = RootDomain.ToLowerInvariant();

		if (host == root || host == $"www.{root}")
			return (TenantHostKind.Apex, null);

		if (host.EndsWith($".{root}"))
		{
			var label = host[..^(root.Length + 1)];
			if (!label.Contains('.'))
				return (TenantHostKind.Company, label);
		}

		return (TenantHostKind.Other, null);
	}

	// The company's alias host if it has one (so the demo site never links to production), else {subdomain}.{RootDomain}.
	public string CompanyHost(string subdomain)
	{
		foreach (var (aliasHost, aliasSubdomain) in HostAliases)
		{
			if (string.Equals(aliasSubdomain, subdomain, StringComparison.OrdinalIgnoreCase))
				return aliasHost.ToLowerInvariant();
		}

		return $"{subdomain}.{RootDomain}".ToLowerInvariant();
	}

	// For links built without a request, e.g. https://iotm.permitpro.app
	public string CompanyBaseUrl(string subdomain)
		=> $"{Scheme}://{CompanyHost(subdomain)}{(Port is int port ? $":{port}" : "")}";
}
