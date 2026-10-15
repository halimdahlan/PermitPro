using System.Text.RegularExpressions;

namespace PermitPro.Core.Helpers;

// Rules for Company.Subdomain, the label in {subdomain}.permitpro.app.
public static partial class CompanySubdomain
{
	public const int MaxLength = 63;

	// Never a company: our own hosts (demo is the demo site until it moves to its own domain), names used
	// by email/DNS (cf-bounce is Cloudflare Email's bounce host) and words that would look like ours.
	public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"www", "admin", "api", "app", "demo", "uat", "staging", "dev", "test", "localhost",
		"mail", "email", "smtp", "imap", "pop", "mx", "webmail", "cf-bounce", "ns1", "ns2",
		"static", "cdn", "assets", "status", "help", "support", "docs", "login", "auth", "account", "permitpro",
	};

	public static string? Normalize(string? value)
		=> string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

	// Error message for a normalized value, or null when it's valid.
	public static string? Validate(string subdomain)
	{
		if (subdomain.Length > MaxLength)
			return $"Subdomain must not exceed {MaxLength} characters.";

		if (!Pattern().IsMatch(subdomain) || subdomain.StartsWith("xn--"))
			return "Use lowercase letters, digits and hyphens only, starting and ending with a letter or digit.";

		if (Reserved.Contains(subdomain))
			return $"\"{subdomain}\" is reserved and can't be used.";

		return null;
	}

	[GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
	private static partial Regex Pattern();
}
