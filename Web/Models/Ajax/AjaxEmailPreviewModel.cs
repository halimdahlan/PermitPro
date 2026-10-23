namespace PermitPro.App.Models.Ajax;

/// <summary>Settings page "Preview" of an email template that is still being edited.</summary>
public class AjaxEmailPreviewModel
{
	/// <summary>The setting's key, e.g. <c>pending_approval_body</c>.</summary>
	public string? Key { get; set; }

	/// <summary>The template text in the editor (blank previews the built-in default).</summary>
	public string? Html { get; set; }
}
