#nullable enable

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using PermitPro.Core.Data;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PermitPro.Core.Services.LegacyPermitPdf;

/// <summary>
/// PDF export for old-system permits (created before the new-form cutover), from the saved
/// <c>PermitForm</c> JSON. Drawn with QuestPDF — no browser, no network requests.
/// </summary>
public sealed class LegacyPermitPdfService : IPermitPdfService
{
	private readonly ApplicationDbContext _dbContext;
	private readonly PTWSettings _ptwSettings;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly ILogger<LegacyPermitPdfService> _logger;

	public LegacyPermitPdfService(
		ApplicationDbContext dbContext
		, PTWSettings ptwSettings
		, IWebHostEnvironment webHostEnvironment
		, ILogger<LegacyPermitPdfService> logger)
	{
		_dbContext = dbContext;
		_ptwSettings = ptwSettings;
		_webHostEnvironment = webHostEnvironment;
		_logger = logger;

		QuestPDF.Settings.License = LicenseType.Community;
		PdfFonts.EnsureRegistered();
	}

	public async Task<byte[]?> GenerateAsync(Guid permitId, CancellationToken ct = default)
	{
		var permit = await _dbContext.Permits
			.AsNoTracking()
			.Include(e => e.Company)
			.FirstOrDefaultAsync(e => e.Id == permitId, ct);

		if (permit == null)
		{
			_logger.LogWarning("PDF export requested for non-existent permit {PermitId}", permitId);
			return null;
		}

		var pdf = Render(permit);

		_logger.LogInformation("PDF generated for permit {PermitId}, size {Bytes} bytes", permit.Id, pdf.Length);
		return pdf;
	}

	/// <summary>Draws an already-loaded permit (with <c>Company</c> included).</summary>
	public byte[] Render(Entities.Permit permit) => new LegacyPermitPdfDocument(BuildModel(permit)).GeneratePdf();

	private LegacyPermitPdfModel BuildModel(Entities.Permit permit)
	{
		var form = PermitFormReader.Parse(permit.PermitForm);
		var attached = form.Items("general.certificates").ToList();

		var checklist = new List<CertificateChecklistItem>();
		var certificates = new List<AttachedCertificate>();

		// PTWSettings.Certificates (appsettings.json) defines the A–H order and titles.
		foreach (var item in _ptwSettings.Certificates)
		{
			var data = attached.FirstOrDefault(c => string.Equals(c.Raw("name"), item.Name, StringComparison.Ordinal));
			checklist.Add(new CertificateChecklistItem(item.Description, item.Title, data != null));

			if (data != null)
				certificates.Add(new AttachedCertificate(item.Name, item.Description, data));
		}

		var img = Path.Combine(_webHostEnvironment.WebRootPath, "img");

		return new LegacyPermitPdfModel
		{
			PermitNo = $"PTW{permit.RunningNumber:000000}",
			DateIssued = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen).ToString("dd/MM/yyyy @ HH:mm"),
			CompanyName = permit.Company?.Description ?? permit.Company?.Name ?? string.Empty,
			Logo = PdfLogo.Load(_webHostEnvironment.WebRootPath, permit.Company?.LogoFileName),
			CheckMark = File.ReadAllBytes(Path.Combine(img, "cb-mark.png")),
			CheckEmpty = File.ReadAllBytes(Path.Combine(img, "cb-empty.png")),
			Form = form,
			CertificateChecklist = checklist,
			Certificates = certificates,
		};
	}
}
