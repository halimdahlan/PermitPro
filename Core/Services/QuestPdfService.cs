#nullable disable

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using System.Text.Json;

namespace PermitPro.Core.Services;

public class QuestPdfService : IQuestPdfService
{
	private readonly ApplicationDbContext _db;
	private readonly IWebHostEnvironment _env;
	private readonly IAppSettingsService _appSettings;

	public QuestPdfService(
		 ApplicationDbContext db,
		 IWebHostEnvironment env,
		 IAppSettingsService appSettings)
	{
		_db = db;
		_env = env;
		_appSettings = appSettings;

		// Community licence is sufficient for internal business documents
		QuestPDF.Settings.License = LicenseType.Community;
	}


	public async Task<byte[]> GenerateAsync(Permit permit, CancellationToken ct = default)
	{
		// Load template and company
		var templateId = ExtractTemplateId(permit.PermitDataJson);
		var template = templateId != Guid.Empty
			 ? await _db.FormTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct)
			 : await _db.FormTemplates.AsNoTracking()
				  .FirstOrDefaultAsync(t => t.CompanyId == permit.Company.Id
						&& t.TemplateType == FormTemplateTypeEnum.Permit && t.IsActive, ct);

		var company = permit.Company
			 ?? await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == permit.Company.Id, ct);

		// Parse field values
		var fields = ParseFields(permit.PermitDataJson);

		// Parse layout
		LayoutRoot layout = null;
		if (template != null)
		{
			try { layout = JsonSerializer.Deserialize<LayoutRoot>(template.LayoutJson, JsonOpts); }
			catch { }
		}

		// Parse header/footer config
		var headerCfg = ParseConfig<HeaderConfig>(template?.HeaderConfigJson);
		var footerCfg = ParseConfig<FooterConfig>(template?.FooterConfigJson);

		// Collect all control definitions referenced in the layout
		var controlIds = layout?.Rows
			 .SelectMany(r => r.Columns)
			 .Where(c => c.ControlId.HasValue)
			 .Select(c => c.ControlId.Value)
			 .Distinct()
			 .ToList() ?? new();

		var controls = controlIds.Count > 0
			 ? await _db.FormControls.AsNoTracking()
				  .Where(c => controlIds.Contains(c.Id))
				  .ToDictionaryAsync(c => c.Id, ct)
			 : new Dictionary<Guid, FormControl>();

		var logoPath = Path.Combine(_env.WebRootPath, "img", "app-logo.jpg");

		var doc = Document.Create(container =>
		{
			container.Page(page =>
			  {
				 page.Size(PageSizes.A4);
				 page.MarginTop(2f, Unit.Centimetre);
				 page.MarginBottom(1.5f, Unit.Centimetre);
				 page.MarginLeft(1.5f, Unit.Centimetre);
				 page.MarginRight(1.5f, Unit.Centimetre);

				  // ── Header ─────────────────────────────────────────────────
				 page.Header().Element(header => RenderHeader(
						 header, headerCfg, company, permit, logoPath));

				  // ── Content ────────────────────────────────────────────────
				 page.Content().Element(content =>
					{
					  content.PaddingVertical(0.5f, Unit.Centimetre).Column(col =>
						 {
							if (layout == null || !layout.Rows.Any())
							{
								col.Item().Text("No form layout defined for this permit.")
									  .FontSize(10).FontColor(Colors.Grey.Medium);
								return;
							}

							foreach (var row in layout.Rows)
							{
								col.Item().PaddingBottom(6).Row(rowContainer =>
								  {
									  var colCount = Math.Max(1, row.ColumnCount);
									  foreach (var colDef in row.Columns)
									  {
										  rowContainer.RelativeItem().PaddingHorizontal(3)
												  .Element(cell => RenderCell(
														cell, colDef, controls, fields));
									  }
									  // Pad missing column slots — must have content to satisfy QuestPDF constraints
									  for (int i = row.Columns.Count; i < colCount; i++)
										  rowContainer.RelativeItem().Text("");
								  });
							}
						});
				  });

				  // ── Footer ─────────────────────────────────────────────────
				 page.Footer().Element(footer => RenderFooter(footer, footerCfg));
			 });
		});

		return doc.GeneratePdf();
	}


	// ── Header ────────────────────────────────────────────────────────────────

	private static void RenderHeader(
		 IContainer header,
		 HeaderConfig cfg,
		 Company company,
		 Permit permit,
		 string logoPath)
	{
		header.BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
				.PaddingBottom(6).Row(row =>
		{
			// Logo
			if ((cfg?.ShowCompanyLogo ?? true) && File.Exists(logoPath))
			{
				row.ConstantItem(60).Image(logoPath).FitWidth();
				row.ConstantItem(8).Text("");   // spacer — must have content
			}

			// Centre text block
			row.RelativeItem().Column(col =>
			  {
				 var customText = cfg?.CustomText ?? "Permit to Work";
				 col.Item().Text(customText.ToUpper())
						.Bold().FontSize(13);

				 if (!string.IsNullOrWhiteSpace(company?.Description))
					 col.Item().Text(company.Description).Bold().FontSize(10).FontColor(Colors.Grey.Darken2);

				 if (cfg?.ShowSiteName == true && permit.Site != null)
					 col.Item().Text(permit.Site.Name).FontSize(9).FontColor(Colors.Grey.Darken1);
			 });

			// Right block — permit no + date
			row.ConstantItem(130).Column(col =>
			  {
				 if (cfg?.ShowPermitNumber != false)
					 col.Item().AlignRight().Text($"PTW{permit.RunningNumber:000000}")
							.Bold().FontSize(11);

				 if (cfg?.ShowDate != false)
					 col.Item().AlignRight()
							.Text(GeneralHelper.GetDateInTimeZone(permit.CreatedWhen).ToString("dd MMM yyyy"))
							.FontSize(8).FontColor(Colors.Grey.Darken1);

				  // Ensure column always has at least one item
				 col.Item().Text("");
			 });
		});
	}


	// ── Cell ──────────────────────────────────────────────────────────────────

	private static void RenderCell(
		 IContainer cell,
		 ColumnDef colDef,
		 Dictionary<Guid, FormControl> controls,
		 Dictionary<string, JsonElement> fields)
	{
		cell.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
			 .Padding(5).Column(col =>
		{
			if (!colDef.ControlId.HasValue
					|| !controls.TryGetValue(colDef.ControlId.Value, out var ctrl))
			{
				col.Item().Text("").FontSize(9);
				return;
			}

			var label = colDef.LabelOverride ?? ctrl.Label;
			var req = colDef.IsRequiredOverride ?? ctrl.IsRequired;

			col.Item().Text(text =>
			  {
				 text.Span(label).Bold().FontSize(9);
				 if (req) text.Span(" *").FontColor(Colors.Red.Medium).FontSize(9);
			 });
			col.Item().PaddingTop(2).Element(val =>
					RenderValue(val, colDef.Id, ctrl, fields));
		});
	}

	private static void RenderValue(
		 IContainer container,
		 string colId,
		 FormControl ctrl,
		 Dictionary<string, JsonElement> fields)
	{
		fields.TryGetValue(colId, out var valEl);
		var raw = valEl.ValueKind != JsonValueKind.Undefined ? valEl : default;

		switch (ctrl.ControlType)
		{
			case FormControlTypeEnum.Checkbox:
				{
					var isChecked = raw.ValueKind == JsonValueKind.True
						 || (raw.ValueKind == JsonValueKind.String
							  && raw.GetString()?.ToLower() == "true");
					container.Row(r =>
					{
						r.ConstantItem(12).Height(12).Border(0.5f).BorderColor(Colors.Grey.Darken1)
						  .Background(isChecked ? Colors.Black : Colors.White);
						r.ConstantItem(4);
						r.RelativeItem().Text(isChecked ? "Yes" : "No").FontSize(9);
					});
					break;
				}

			case FormControlTypeEnum.MultiChoice:
				{
					var selected = new HashSet<string>();
					if (raw.ValueKind == JsonValueKind.Array)
						foreach (var el in raw.EnumerateArray())
							if (el.ValueKind == JsonValueKind.String) selected.Add(el.GetString());

					var opts = ParseOptions(ctrl.OptionsJson);
					container.Column(col =>
					{
						foreach (var opt in opts)
						{
							col.Item().Row(r =>
							 {
								  var chk = selected.Contains(opt.Value);
								  r.ConstantItem(10).Height(10).Border(0.5f).BorderColor(Colors.Grey.Darken1)
									 .Background(chk ? Colors.Black : Colors.White);
								  r.ConstantItem(4);
								  r.RelativeItem().Text(opt.Label).FontSize(8);
							  });
						}
					});
					break;
				}

			case FormControlTypeEnum.Select:
				{
					var val = raw.ValueKind == JsonValueKind.String ? raw.GetString() : "";
					var opts = ParseOptions(ctrl.OptionsJson);
					var match = opts.FirstOrDefault(o => o.Value == val);
					var display = match != default ? match.Label : (val ?? "—");
					container.Text(display).FontSize(9);
					break;
				}

			case FormControlTypeEnum.DateTimeInput:
				{
					var val = raw.ValueKind == JsonValueKind.String ? raw.GetString() : "";
					if (!string.IsNullOrEmpty(val) && DateTime.TryParse(val, out var dt))
						container.Text(dt.ToLocalTime().ToString("dd MMM yyyy HH:mm")).FontSize(9);
					else
						container.Text(val ?? "—").FontSize(9);
					break;
				}

			case FormControlTypeEnum.FileUpload:
				{
					if (raw.ValueKind == JsonValueKind.Array)
					{
						container.Column(col =>
						{
							foreach (var el in raw.EnumerateArray())
								col.Item().Text($"• {el.GetString() ?? "file"}").FontSize(8);
						});
					}
					else
						container.Text("—").FontSize(9);
					break;
				}

			default: // TextBox, TextArea
				{
					var val = raw.ValueKind == JsonValueKind.String ? raw.GetString() : "";
					container.Text(string.IsNullOrWhiteSpace(val) ? "—" : val)
						 .FontSize(9).FontColor(Colors.Black);
					break;
				}
		}
	}


	// ── Footer ────────────────────────────────────────────────────────────────

	private static void RenderFooter(IContainer footer, FooterConfig cfg)
	{
		footer.BorderTop(1).BorderColor(Colors.Grey.Lighten2)
				.PaddingTop(4).Row(row =>
		{
			row.RelativeItem().Text(cfg?.CustomText ?? "")
				  .FontSize(7).FontColor(Colors.Grey.Medium);

			row.ConstantItem(80).AlignRight().Text(text =>
			  {
				 if (cfg?.ShowPrintDate == true)
					 text.Span($"Printed: {DateTime.Now:dd MMM yyyy}  ")
							 .FontSize(7).FontColor(Colors.Grey.Medium);
				 if (cfg?.ShowPageNumbers != false)
				 {
					 text.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Medium);
					 text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Medium);
					 text.TotalPages().FontSize(7).FontColor(Colors.Grey.Medium);
				 }
			 });
		});
	}


	// ── Helpers ───────────────────────────────────────────────────────────────

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private static Guid ExtractTemplateId(string permitDataJson)
	{
		if (string.IsNullOrWhiteSpace(permitDataJson)) return Guid.Empty;
		try
		{
			using var doc = JsonDocument.Parse(permitDataJson);
			if (doc.RootElement.TryGetProperty("templateId", out var el) && el.TryGetGuid(out var id))
				return id;
		}
		catch { }
		return Guid.Empty;
	}

	private static Dictionary<string, JsonElement> ParseFields(string permitDataJson)
	{
		if (string.IsNullOrWhiteSpace(permitDataJson)) return new();
		try
		{
			using var doc = JsonDocument.Parse(permitDataJson);
			if (doc.RootElement.TryGetProperty("fields", out var fieldsEl)
				 && fieldsEl.ValueKind == JsonValueKind.Object)
			{
				return fieldsEl.EnumerateObject()
					 .ToDictionary(p => p.Name, p => p.Value.Clone());
			}
		}
		catch { }
		return new();
	}

	private static T ParseConfig<T>(string json) where T : class
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
		catch { return null; }
	}

	private static List<(string Value, string Label)> ParseOptions(string optionsJson)
	{
		if (string.IsNullOrWhiteSpace(optionsJson)) return new();
		try
		{
			using var doc = JsonDocument.Parse(optionsJson);
			return doc.RootElement.EnumerateArray()
				 .Select(el => (
					  Value: el.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "",
					  Label: el.TryGetProperty("label", out var l) ? l.GetString() ?? "" : ""))
				 .ToList();
		}
		catch { return new(); }
	}


	// ── Layout / config POCOs ─────────────────────────────────────────────────

	private class LayoutRoot
	{
		public List<RowDef> Rows { get; set; } = new();
	}

	private class RowDef
	{
		public string Id { get; set; }
		public int ColumnCount { get; set; } = 1;
		public List<ColumnDef> Columns { get; set; } = new();
	}

	private class ColumnDef
	{
		public string Id { get; set; }
		public Guid? ControlId { get; set; }
		public string LabelOverride { get; set; }
		public bool? IsRequiredOverride { get; set; }
	}

	private class HeaderConfig
	{
		public bool ShowCompanyLogo { get; set; } = true;
		public string LogoPosition { get; set; } = "left";
		public bool ShowPermitNumber { get; set; } = true;
		public bool ShowSiteName { get; set; } = true;
		public bool ShowDate { get; set; } = true;
		public string CustomText { get; set; }
	}

	private class FooterConfig
	{
		public bool ShowPageNumbers { get; set; } = true;
		public bool ShowPrintDate { get; set; }
		public string CustomText { get; set; }
	}
}
