namespace PermitPro.Core.Interfaces;

public sealed record RenderedEmail(string Subject, string Body);

/// <summary>
/// Renders an email from the template stored in the app settings (category <c>email_templates</c>,
/// keys <c>{name}_subject</c> and <c>{name}_body</c>; company row overrides the global one).
/// A missing, blank or malformed setting falls back to the built-in default, so a bad edit never stops an email.
/// </summary>
public interface IEmailTemplateService
{
	Task<RenderedEmail> RenderAsync(Guid companyId, string templateName, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default);

	/// <summary>
	/// Renders a template being edited with sample data, for the Settings preview. <paramref name="settingKey"/> is the
	/// setting's key (e.g. <c>pending_approval_body</c>); a blank template previews the built-in default.
	/// Returns null for a key that isn't an email template; throws <see cref="FormatException"/> when the template is malformed.
	/// </summary>
	string? Preview(string settingKey, string? template);
}

public static class EmailTemplateNames
{
	public const string PendingApproval = "pending_approval";
}
