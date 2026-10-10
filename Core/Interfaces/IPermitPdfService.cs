namespace PermitPro.Core.Interfaces;

/// <summary>PDF export of an old-system permit (created before the new-form cutover).</summary>
public interface IPermitPdfService
{
	/// <summary>The permit as a PDF, or null when the permit doesn't exist.</summary>
	Task<byte[]?> GenerateAsync(Guid permitId, CancellationToken ct = default);
}
