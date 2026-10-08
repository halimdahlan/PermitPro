using MailKit.Net.Smtp;
using MailKit.Security;

namespace PermitPro.Core.Helpers;

public static class SmtpHelper
{
	// Fail fast instead of MailKit's 2-minute default, so a misconfigured server doesn't hang requests.
	private const int TimeoutMilliseconds = 30_000;

	/// <summary>
	/// TLS mode for an SMTP port. Port 465 is implicit TLS (SMTPS) — the only mode Cloudflare Email
	/// Service accepts (smtp.mx.cloudflare.net:465; plaintext and STARTTLS are rejected). Other ports
	/// keep the previous behaviour: STARTTLS when the server offers it.
	/// </summary>
	public static SecureSocketOptions GetSecureSocketOptions(int port)
		=> port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

	/// <summary>Connects using the TLS mode appropriate for the port. All SMTP connections should go through here.</summary>
	public static Task ConnectAsync(SmtpClient client, string host, int port, CancellationToken cancellationToken = default)
	{
		client.Timeout = TimeoutMilliseconds;
		return client.ConnectAsync(host, port, GetSecureSocketOptions(port), cancellationToken);
	}
}
