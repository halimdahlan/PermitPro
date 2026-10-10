#nullable enable

using PermitPro.Core.Helpers;

namespace PermitPro.Core.Services.LegacyPermitPdf;

/// <summary>Everything the old-system permit PDF needs, loaded up front.</summary>
internal sealed class LegacyPermitPdfModel
{
	public required string PermitNo { get; init; }
	public required string DateIssued { get; init; }
	public required string CompanyName { get; init; }
	public PdfLogo? Logo { get; init; }
	public required byte[] CheckMark { get; init; }
	public required byte[] CheckEmpty { get; init; }
	public required PermitFormReader Form { get; init; }

	/// <summary>The A–H list on page 1 (attached or not).</summary>
	public required IReadOnlyList<CertificateChecklistItem> CertificateChecklist { get; init; }

	/// <summary>Attached certificates, in A–H order; each gets its own section.</summary>
	public required IReadOnlyList<AttachedCertificate> Certificates { get; init; }
}

internal sealed record CertificateChecklistItem(string Letter, string Title, bool Attached);

internal sealed record AttachedCertificate(string Name, string Letter, PermitFormReader Data);
