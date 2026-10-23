using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

namespace PermitPro.Core.Services;

public class EmailTemplateService : IEmailTemplateService
{
	public const string CategoryName = "email_templates";

	private readonly IAppSettingsService _appSettings;
	private readonly ILogger<EmailTemplateService> _logger;

	public EmailTemplateService(IAppSettingsService appSettings, ILogger<EmailTemplateService> logger)
	{
		_appSettings = appSettings;
		_logger = logger;
	}

	public async Task<RenderedEmail> RenderAsync(Guid companyId, string templateName, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default)
	{
		var defaults = EmailTemplateDefaults.Get(templateName);

		var subjectTemplate = await _appSettings.GetValueAsync(companyId, CategoryName, $"{templateName}_subject");
		var bodyTemplate = await _appSettings.GetValueAsync(companyId, CategoryName, $"{templateName}_body");

		var subject = TryRender(subjectTemplate, model, encode: false, templateName, "subject")
			?? EmailTemplate.Render(defaults.Subject, model, encode: false);
		var body = TryRender(bodyTemplate, model, encode: true, templateName, "body")
			?? EmailTemplate.Render(defaults.Body, model, encode: true);

		return new RenderedEmail(subject.ReplaceLineEndings(" ").Trim(), body);
	}

	public string? Preview(string settingKey, string? template)
	{
		const string suffix = "_body";
		if (settingKey == null || !settingKey.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;

		var defaults = EmailTemplateDefaults.TryGet(settingKey[..^suffix.Length].ToLowerInvariant());
		if (defaults == null) return null;

		return EmailTemplate.Render(string.IsNullOrWhiteSpace(template) ? defaults.Body : template, defaults.Sample);
	}

	// Null = use the built-in default (setting blank, or its template is malformed).
	private string? TryRender(string? template, IReadOnlyDictionary<string, object?> model, bool encode, string templateName, string part)
	{
		if (string.IsNullOrWhiteSpace(template)) return null;

		try
		{
			return EmailTemplate.Render(template, model, encode);
		}
		catch (FormatException ex)
		{
			_logger.LogWarning("Email template '{Template}' ({Part}) is malformed, using the built-in default: {Message}", templateName, part, ex.Message);
			return null;
		}
	}
}

/// <summary>Built-in templates. The body HTML ships inside the assembly (Resources/EmailTemplates).</summary>
public static class EmailTemplateDefaults
{
	/// <param name="Sample">Made-up values for the Settings preview.</param>
	public sealed record Entry(string Name, string DisplayName, string Subject, string Body, IReadOnlyDictionary<string, object?> Sample);

	public static IReadOnlyList<Entry> All { get; } =
	[
		new(EmailTemplateNames.PendingApproval, "Pending Approval", "Approval required: permit {{permit_no}}", LoadBody("pending-approval.html"), PendingApprovalSample()),
	];

	public static Entry? TryGet(string name) => All.FirstOrDefault(e => e.Name == name);

	public static Entry Get(string name) =>
		TryGet(name) ?? throw new ArgumentException($"Unknown email template '{name}'.", nameof(name));

	private static Dictionary<string, object?> PendingApprovalSample()
	{
		static IReadOnlyDictionary<string, object?> Step(string number, string name, string status, string color) =>
			new Dictionary<string, object?> { ["number"] = number, ["name"] = name, ["status"] = status, ["color"] = color };

		return new Dictionary<string, object?>
		{
			["recipient_name"] = "Aisyah Rahman",
			["company_name"] = "Sample Company Sdn Bhd",
			["permit_no"] = "PTW000123",
			["permit_title"] = "Hot work: welding of pipe support in Block A",
			["location"] = "Plant 2 / Boiler House",
			["start_date"] = "21 Oct 2026",
			["end_date"] = "23 Oct 2026",
			["submitted_by"] = "Halim Dahlan",
			["date_submitted"] = "20 Oct 2026, 04:12 PM",
			["workflow_name"] = "Standard Permit Workflow",
			["current_step"] = "HSE Officer",
			["step_number"] = "2",
			["step_total"] = "3",
			["next_step"] = "Plant Manager",
			["other_approvers"] = "Daniel Lee, Mei Ling Tan",
			["approval_link"] = "https://permitpro.app/account/login?entity=permits&origin=email",
			["steps"] = new List<IReadOnlyDictionary<string, object?>>
			{
				Step("1", "Supervisor Review", "Completed", "#2e9e5b"),
				Step("2", "HSE Officer", "Awaiting approval", "#d97706"),
				Step("3", "Plant Manager", "Upcoming", "#8896a8"),
			},
		};
	}

	private static string LoadBody(string fileName)
	{
		using var stream = typeof(EmailTemplateDefaults).Assembly.GetManifestResourceStream($"EmailTemplates.{fileName}")
			?? throw new InvalidOperationException($"Embedded email template '{fileName}' not found.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}
}

public static class EmailTemplateSeed
{
	/// <summary>
	/// Adds the global "email_templates" category and each template's subject/body settings if they are
	/// missing. Never overwrites an existing row, so edits made in Settings survive restarts.
	/// </summary>
	public static async Task EnsureDefaultsAsync(IServiceProvider sp, CancellationToken ct = default)
	{
		using var scope = sp.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

		var category = await db.AppSettingCategories
			.FirstOrDefaultAsync(c => c.CompanyId == null && c.Name == EmailTemplateService.CategoryName, ct);

		if (category == null)
		{
			var nextOrder = (await db.AppSettingCategories.Where(c => c.CompanyId == null).MaxAsync(c => (int?)c.SortOrder, ct) ?? 0) + 1;
			category = new AppSettingCategory
			{
				Id = Guid.NewGuid(),
				CompanyId = null,
				Name = EmailTemplateService.CategoryName,
				DisplayName = "Email Templates",
				Description = "Subject and HTML of the emails the system sends",
				Icon = "fa-envelope-open-text",
				SortOrder = nextOrder,
			};
			db.AppSettingCategories.Add(category);
		}

		var existingKeys = (await db.AppSettings
			.Where(s => s.CompanyId == null && s.CategoryId == category.Id)
			.Select(s => s.Key)
			.ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

		var order = 1;
		foreach (var t in EmailTemplateDefaults.All)
		{
			if (!existingKeys.Contains($"{t.Name}_subject"))
				db.AppSettings.Add(NewSetting(category.Id, $"{t.Name}_subject", $"{t.DisplayName} - Subject", "text", t.Subject, order));
			order++;

			if (!existingKeys.Contains($"{t.Name}_body"))
				db.AppSettings.Add(NewSetting(category.Id, $"{t.Name}_body", $"{t.DisplayName} - Email (HTML)", "html", t.Body, order));
			order++;
		}

		if (db.ChangeTracker.HasChanges())
			await db.SaveChangesAsync(ct);
	}

	private static AppSetting NewSetting(Guid categoryId, string key, string displayName, string dataType, string value, int sortOrder) => new()
	{
		Id = Guid.NewGuid(),
		CategoryId = categoryId,
		CompanyId = null,
		Key = key,
		DisplayName = displayName,
		DataType = dataType,
		IsEncrypted = false,
		SortOrder = sortOrder,
		Value = value,
	};
}
