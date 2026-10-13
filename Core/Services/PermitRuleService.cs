using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;
using PermitPro.Core.Models;
using PermitPro.Core.Models.PermitRules;

using Scriban;

using System.Net.Mail;
using System.Text.Json;

namespace PermitPro.Core.Services;

// Preset permit rules (Overdue, Auto-archive). RunAllAsync is called every few minutes by
// PermitRulesBackgroundService; there is no HTTP request, so links come from App:BaseUrl.
public class PermitRuleService : IPermitRuleService
{
	private static readonly PermitStatusEnum[] _closedStatuses =
		[PermitStatusEnum.Closed, PermitStatusEnum.ClosedNoAction, PermitStatusEnum.Rejected];

	private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

	private const int PreviewSampleSize = 10;

	private readonly ApplicationDbContext _db;
	private readonly IMessageService _messageService;
	private readonly INotificationPushService _pushService;
	private readonly IWebHostEnvironment _env;
	private readonly IConfiguration _configuration;
	private readonly ILogger<PermitRuleService> _logger;

	public PermitRuleService(
		ApplicationDbContext db,
		IMessageService messageService,
		INotificationPushService pushService,
		IWebHostEnvironment env,
		IConfiguration configuration,
		ILogger<PermitRuleService> logger)
	{
		_db = db;
		_messageService = messageService;
		_pushService = pushService;
		_env = env;
		_configuration = configuration;
		_logger = logger;
	}


	// ── Presets ───────────────────────────────────────────────────────────────

	public async Task<List<PermitRule>> GetOrCreatePresetsAsync(Guid companyId, CancellationToken ct = default)
	{
		var rules = await _db.PermitRules.Where(r => r.CompanyId == companyId).ToListAsync(ct);

		var missing = Enum.GetValues<PermitRuleTypeEnum>().Where(t => rules.All(r => r.RuleType != t)).ToList();
		foreach (var type in missing)
		{
			var rule = new PermitRule
			{
				CompanyId = companyId,
				RuleType = type,
				IsEnabled = false,
				SettingsJson = DefaultSettingsJson(type),
			};
			_db.PermitRules.Add(rule);
			rules.Add(rule);
		}

		if (missing.Count > 0)
			await _db.SaveChangesAsync(ct);

		return rules.OrderBy(r => r.RuleType).ToList();
	}

	public async Task SaveAsync(Guid companyId, PermitRuleTypeEnum ruleType, bool isEnabled, string? settingsJson, CancellationToken ct = default)
	{
		var rule = (await GetOrCreatePresetsAsync(companyId, ct)).Single(r => r.RuleType == ruleType);

		rule.SettingsJson = NormalizeSettings(ruleType, settingsJson);
		rule.IsEnabled = isEnabled;

		await _db.SaveChangesAsync(ct);
	}

	private static string DefaultSettingsJson(PermitRuleTypeEnum type) => type switch
	{
		PermitRuleTypeEnum.Overdue => JsonSerializer.Serialize(new OverdueRuleSettings()),
		PermitRuleTypeEnum.AutoArchive => JsonSerializer.Serialize(new AutoArchiveRuleSettings()),
		_ => "{}",
	};

	private static string NormalizeSettings(PermitRuleTypeEnum type, string? settingsJson)
	{
		switch (type)
		{
			case PermitRuleTypeEnum.Overdue:
				var overdue = Parse<OverdueRuleSettings>(settingsJson);
				if (overdue.RepeatEveryDays is < 1 or > 365)
					throw new ArgumentException("Repeat every must be between 1 and 365 days.");
				var invalid = SplitEmails(overdue.ExtraEmails).Where(e => !IsValidEmail(e)).ToList();
				if (invalid.Count > 0)
					throw new ArgumentException($"Not a valid email address: {string.Join(", ", invalid)}");
				overdue.ExtraEmails = string.Join("; ", SplitEmails(overdue.ExtraEmails));
				return JsonSerializer.Serialize(overdue);

			case PermitRuleTypeEnum.AutoArchive:
				var archive = Parse<AutoArchiveRuleSettings>(settingsJson);
				if (archive.DaysAfterClosed is < 1 or > 3650)
					throw new ArgumentException("Days after closing must be between 1 and 3650.");
				return JsonSerializer.Serialize(archive);

			default:
				throw new ArgumentException("Unknown rule.");
		}
	}

	private static T Parse<T>(string? json) where T : new()
	{
		if (string.IsNullOrWhiteSpace(json)) return new T();
		try
		{
			return JsonSerializer.Deserialize<T>(json, _json) ?? new T();
		}
		catch (JsonException)
		{
			return new T();
		}
	}

	private static List<string> SplitEmails(string? emails) =>
		(emails ?? string.Empty)
			.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

	private static bool IsValidEmail(string email) =>
		MailAddress.TryCreate(email, out var address) && address.Address.Equals(email, StringComparison.OrdinalIgnoreCase);


	// ── Candidates (shared by the dry run and the runner) ─────────────────────

	// End date passed and not closed / rejected (the definition Reports uses for "overdue").
	private IQueryable<Permit> OverdueQuery(Guid companyId, DateTime nowUtc) =>
		_db.Permits.AsNoTracking().Where(p =>
			p.CompanyId == companyId &&
			!p.IsArchived &&
			p.EndDateTime != null &&
			p.EndDateTime < nowUtc &&
			!_closedStatuses.Contains(p.PermitStatus));

	// Member-init class (not a positional record) so EF can keep filtering and sorting on it.
	private sealed class ClosedPermit
	{
		public Guid Id { get; init; }
		public int RunningNumber { get; init; }
		public string? Description { get; init; }
		public PermitStatusEnum Status { get; init; }
		public DateTime ClosedWhen { get; init; }
	}

	// Closed, closed (no action) or rejected, not yet archived, with the date it was closed:
	// rejection date, else the latest Closed/Rejected workflow history entry, else the last update.
	private IQueryable<ClosedPermit> ClosedQuery(Guid companyId) =>
		_db.Permits.AsNoTracking()
			.Where(p => p.CompanyId == companyId && !p.IsArchived && _closedStatuses.Contains(p.PermitStatus))
			.Select(p => new ClosedPermit
			{
				Id = p.Id,
				RunningNumber = p.RunningNumber,
				Description = p.Description,
				Status = p.PermitStatus,
				ClosedWhen = (p.PermitStatus == PermitStatusEnum.Rejected ? p.RejectedDateTime : null)
					?? p.WorkflowHistories
						.Where(h => h.Status == WorkflowStatusEnum.Closed || h.Status == WorkflowStatusEnum.Rejected)
						.Max(h => (DateTime?)h.CreatedWhen)
					?? p.UpdatedWhen
					?? p.CreatedWhen,
			});

	public async Task<PermitRulePreview> PreviewAsync(Guid companyId, PermitRuleTypeEnum ruleType, string? settingsJson, CancellationToken ct = default)
	{
		var now = DateTime.UtcNow;

		if (settingsJson == null)
			settingsJson = (await GetOrCreatePresetsAsync(companyId, ct)).Single(r => r.RuleType == ruleType).SettingsJson;

		if (ruleType == PermitRuleTypeEnum.Overdue)
		{
			var query = OverdueQuery(companyId, now);
			return new PermitRulePreview
			{
				Count = await query.CountAsync(ct),
				Sample = await query
					.OrderBy(p => p.EndDateTime)
					.Take(PreviewSampleSize)
					.Select(p => new PermitRulePreviewItem
					{
						Id = p.Id,
						RunningNumber = p.RunningNumber,
						Description = p.Description,
						Status = p.PermitStatus.ToString(),
						Date = p.EndDateTime,
					})
					.ToListAsync(ct),
			};
		}

		var cutoff = now.AddDays(-Parse<AutoArchiveRuleSettings>(settingsJson).DaysAfterClosed);
		var closed = ClosedQuery(companyId).Where(p => p.ClosedWhen <= cutoff);
		return new PermitRulePreview
		{
			Count = await closed.CountAsync(ct),
			Sample = await closed
				.OrderBy(p => p.ClosedWhen)
				.Take(PreviewSampleSize)
				.Select(p => new PermitRulePreviewItem
				{
					Id = p.Id,
					RunningNumber = p.RunningNumber,
					Description = p.Description,
					Status = p.Status.ToString(),
					Date = p.ClosedWhen,
				})
				.ToListAsync(ct),
		};
	}


	// ── Runner ────────────────────────────────────────────────────────────────

	public async Task RunAllAsync(CancellationToken ct = default)
	{
		// During an IIS app pool recycle two instances can overlap: only one runs the rules at a time.
		await _db.Database.OpenConnectionAsync(ct);
		try
		{
			if (!await TryAcquireRunLockAsync(ct))
			{
				_logger.LogInformation("Permit rules: another instance is running them, skipped.");
				return;
			}

			var rules = await _db.PermitRules
				.AsNoTracking()
				.Where(r => r.IsEnabled && r.Company != null && r.Company.IsActive)
				.OrderBy(r => r.CompanyId).ThenBy(r => r.RuleType)
				.ToListAsync(ct);

			foreach (var rule in rules)
			{
				var now = DateTime.UtcNow;
				string summary;
				try
				{
					summary = rule.RuleType switch
					{
						PermitRuleTypeEnum.Overdue => await RunOverdueAsync(rule, now, ct),
						PermitRuleTypeEnum.AutoArchive => await RunAutoArchiveAsync(rule, now, ct),
						_ => "Unknown rule type.",
					};
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Permit rule {RuleType} failed for company {CompanyId}", rule.RuleType, rule.CompanyId);
					_db.ChangeTracker.Clear();
					summary = $"Error: {ex.Message}";
					await AddAuditLogAsync(rule.CompanyId, LogTypeEnum.Error, $"{RuleName(rule.RuleType)} rule failed: {ex.Message}", ct);
				}

				// ExecuteUpdate: "last checked" changes every run, so keep it out of the audit fields.
				await _db.PermitRules
					.Where(r => r.Id == rule.Id)
					.ExecuteUpdateAsync(s => s
						.SetProperty(r => r.LastRunWhen, now)
						.SetProperty(r => r.LastRunSummary, summary.Length > 500 ? summary[..500] : summary), ct);
			}
		}
		finally
		{
			await _db.Database.CloseConnectionAsync();
		}
	}

	// Session-level app lock, released when the connection closes.
	private async Task<bool> TryAcquireRunLockAsync(CancellationToken ct)
	{
		await using var cmd = _db.Database.GetDbConnection().CreateCommand();
		cmd.CommandText =
			"DECLARE @r int; " +
			"EXEC @r = sp_getapplock @Resource = 'PermitPro.PermitRules', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; " +
			"SELECT @r;";
		var result = await cmd.ExecuteScalarAsync(ct);
		return Convert.ToInt32(result) >= 0;
	}

	private static string RuleName(PermitRuleTypeEnum type) => type switch
	{
		PermitRuleTypeEnum.Overdue => "Overdue permits",
		PermitRuleTypeEnum.AutoArchive => "Auto-archive",
		_ => type.ToString(),
	};

	private static string PermitNo(int runningNumber) => $"PTW{runningNumber:000000}";

	private string BaseUrl() =>
		(_configuration["App:BaseUrl"] ?? (_env.IsDevelopment() ? "https://localhost:7079" : "https://permitpro.app")).TrimEnd('/');


	// ── Overdue ───────────────────────────────────────────────────────────────

	private sealed class Recipient
	{
		public required string Name { get; init; }
		public required string Email { get; init; }
		public string? UserId { get; init; }
		public List<OverduePermit> Permits { get; } = [];
	}

	private sealed record OverduePermit(Guid Id, int RunningNumber, string? Description, string? Location, DateTime EndDateTime, Guid? CreatedBy);

	private async Task<string> RunOverdueAsync(PermitRule rule, DateTime now, CancellationToken ct)
	{
		var settings = Parse<OverdueRuleSettings>(rule.SettingsJson);
		var companyId = rule.CompanyId;

		var overdue = await OverdueQuery(companyId, now)
			.Select(p => new OverduePermit(p.Id, p.RunningNumber, p.Description, p.Site != null ? p.Site.Name : null, p.EndDateTime!.Value, p.CreatedBy))
			.ToListAsync(ct);

		if (overdue.Count == 0)
			return "No overdue permits.";

		// Notify once, then again every N days, per permit.
		var runs = await _db.PermitRuleRuns.Where(r => r.PermitRuleId == rule.Id).ToDictionaryAsync(r => r.PermitId, ct);
		var repeatBefore = now.AddDays(-settings.RepeatEveryDays);
		var due = overdue
			.Where(p => !runs.TryGetValue(p.Id, out var run) || run.LastRunWhen <= repeatBefore)
			.OrderBy(p => p.EndDateTime)
			.ToList();

		if (due.Count == 0)
			return $"{overdue.Count} overdue, none due for a reminder.";

		var recipients = await BuildRecipientsAsync(companyId, settings, due, ct);

		// PermitRules:SendEmails (default false): until it's turned on, only in-app notifications go out.
		var sendEmails = _configuration.GetValue("PermitRules:SendEmails", false);

		if (recipients.Count > 0)
		{
			var companyName = await _db.Companies.AsNoTracking()
				.Where(c => c.Id == companyId)
				.Select(c => c.Description ?? c.Name)
				.FirstOrDefaultAsync(ct);

			var template = Template.Parse(await File.ReadAllTextAsync(Path.Combine(_env.WebRootPath, "templates", "html", "permit-overdue.html"), ct));
			var baseUrl = BaseUrl();

			foreach (var recipient in recipients)
			{
				var permits = recipient.Permits.OrderBy(p => p.EndDateTime).ToList();
				var subject = permits.Count == 1
					? $"Permit overdue: {PermitNo(permits[0].RunningNumber)}"
					: $"{permits.Count} permits are overdue";

				var body = template.Render(new
				{
					RecipientName = recipient.Name,
					CompanyName = companyName,
					PermitCount = permits.Count,
					RepeatEveryDays = settings.RepeatEveryDays,
					PermitsLink = $"{baseUrl}/{companyId}/permits",
					Permits = permits.Select(p => new
					{
						PermitNo = PermitNo(p.RunningNumber),
						Description = p.Description ?? string.Empty,
						Location = p.Location ?? string.Empty,
						EndDate = GeneralHelper.GetDateInTimeZone(p.EndDateTime).ToString("dd MMM yyyy, hh:mm tt"),
						DaysOverdue = Math.Max(0, (int)Math.Floor((now - p.EndDateTime).TotalDays)),
						Link = $"{baseUrl}/account/login?company={companyId}&entity=permits&id={p.Id}&origin=email",
					}),
				});

				// Errors are caught and written to the audit log by MessageService.
				if (sendEmails)
					await _messageService.SendEmailAsync(new EmailInfo { Name = recipient.Name, Email = recipient.Email, Subject = subject, Body = body }, companyId);

				if (recipient.UserId == null) continue;

				var numbers = permits.Select(p => PermitNo(p.RunningNumber)).ToList();
				var message = numbers.Count == 1
					? $"{numbers[0]} is overdue."
					: $"{numbers.Count} permits are overdue: {string.Join(", ", numbers.Take(5))}{(numbers.Count > 5 ? $" and {numbers.Count - 5} more" : "")}.";

				_db.Notifications.Add(new Notification
				{
					Title = "Overdue permits",
					Message = message,
					// Relative, so it works on whichever host the user is on.
					Url = numbers.Count == 1 ? $"/{companyId}/permits/edit/{permits[0].Id}" : $"/{companyId}/permits",
					NotificationUser = await _db.Users.FindAsync([recipient.UserId], ct),
				});

				await _pushService.PushAsync(recipient.UserId, "Overdue permits", message);
			}
		}

		foreach (var permit in due)
		{
			if (runs.TryGetValue(permit.Id, out var run))
			{
				run.LastRunWhen = now;
				run.RunCount++;
			}
			else
			{
				_db.PermitRuleRuns.Add(new PermitRuleRun
				{
					Id = Guid.NewGuid(),
					PermitRuleId = rule.Id,
					PermitId = permit.Id,
					FirstRunWhen = now,
					LastRunWhen = now,
					RunCount = 1,
				});
			}
		}

		await _db.SaveChangesAsync(ct);

		var list = string.Join(", ", due.Take(20).Select(p => PermitNo(p.RunningNumber))) + (due.Count > 20 ? $" and {due.Count - 20} more" : "");
		var summary = $"Reminded about {due.Count} overdue permit{(due.Count == 1 ? "" : "s")} ({recipients.Count} recipient{(recipients.Count == 1 ? "" : "s")}{(sendEmails ? "" : "; emails off, in-app only")}).";
		await AddAuditLogAsync(companyId, LogTypeEnum.Information, $"Overdue permits rule: {summary} Permits: {list}.", ct);

		return summary;
	}

	// Permit creators get their own permits; admins and extra addresses get all of them.
	private async Task<List<Recipient>> BuildRecipientsAsync(Guid companyId, OverdueRuleSettings settings, List<OverduePermit> due, CancellationToken ct)
	{
		var byEmail = new Dictionary<string, Recipient>(StringComparer.OrdinalIgnoreCase);

		Recipient Add(string name, string email, string? userId)
		{
			if (!byEmail.TryGetValue(email, out var recipient))
			{
				recipient = new Recipient { Name = name, Email = email, UserId = userId };
				byEmail[email] = recipient;
			}
			return recipient;
		}

		void AddPermits(Recipient recipient, IEnumerable<OverduePermit> permits)
		{
			foreach (var permit in permits)
				if (!recipient.Permits.Contains(permit))
					recipient.Permits.Add(permit);
		}

		if (settings.NotifyCreator)
		{
			var creatorIds = due.Where(p => p.CreatedBy.HasValue).Select(p => p.CreatedBy!.Value.ToString().ToLower()).Distinct().ToList();
			var creators = await _db.Users.AsNoTracking()
				.Where(u => u.IsActive && u.Email != null && creatorIds.Contains(u.Id.ToLower()))
				.Select(u => new { u.Id, u.Email, Name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() })
				.ToListAsync(ct);

			foreach (var creator in creators)
			{
				var recipient = Add(creator.Name, creator.Email!, creator.Id);
				AddPermits(recipient, due.Where(p => p.CreatedBy.HasValue && p.CreatedBy.Value.ToString().Equals(creator.Id, StringComparison.OrdinalIgnoreCase)));
			}
		}

		if (settings.NotifyAdmins)
		{
			var admins = await _db.Users.AsNoTracking()
				.Where(u => u.IsActive && u.Email != null && u.UserCompany != null && u.UserCompany.Id == companyId &&
					u.UserRoles.Any(ur => ur.Role!.NormalizedName == "SUPERUSER" || ur.Role!.NormalizedName == "PORTALADMIN"))
				.Select(u => new { u.Id, u.Email, Name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() })
				.ToListAsync(ct);

			foreach (var admin in admins)
				AddPermits(Add(admin.Name, admin.Email!, admin.Id), due);
		}

		foreach (var email in SplitEmails(settings.ExtraEmails))
			AddPermits(Add(email, email, null), due);

		return byEmail.Values.Where(r => r.Permits.Count > 0).ToList();
	}


	// ── Auto-archive ──────────────────────────────────────────────────────────

	private async Task<string> RunAutoArchiveAsync(PermitRule rule, DateTime now, CancellationToken ct)
	{
		var settings = Parse<AutoArchiveRuleSettings>(rule.SettingsJson);
		var cutoff = now.AddDays(-settings.DaysAfterClosed);

		var toArchive = await ClosedQuery(rule.CompanyId)
			.Where(p => p.ClosedWhen <= cutoff)
			.OrderBy(p => p.RunningNumber)
			.Select(p => new { p.Id, p.RunningNumber })
			.ToListAsync(ct);

		if (toArchive.Count == 0)
			return "Nothing to archive.";

		foreach (var chunk in toArchive.Chunk(500))
		{
			var ids = chunk.Select(p => p.Id).ToList();
			await _db.Permits
				.Where(p => ids.Contains(p.Id))
				.ExecuteUpdateAsync(s => s
					.SetProperty(p => p.IsArchived, true)
					.SetProperty(p => p.ArchivedWhen, now), ct);
		}

		var list = string.Join(", ", toArchive.Take(20).Select(p => PermitNo(p.RunningNumber))) + (toArchive.Count > 20 ? $" and {toArchive.Count - 20} more" : "");
		var summary = $"Archived {toArchive.Count} permit{(toArchive.Count == 1 ? "" : "s")}.";
		await AddAuditLogAsync(rule.CompanyId, LogTypeEnum.Information, $"Auto-archive rule (closed {settings.DaysAfterClosed}+ days): {summary} Permits: {list}.", ct);

		return summary;
	}


	private async Task AddAuditLogAsync(Guid companyId, LogTypeEnum type, string message, CancellationToken ct)
	{
		_db.AuditLogs.Add(new AuditLog
		{
			Id = Guid.NewGuid(),
			LogType = type,
			Category = "PERMIT_RULE",
			EntityName = "PermitRule",
			Description = message,
			CompanyId = companyId,
			CreatedWhen = DateTime.UtcNow,
		});
		await _db.SaveChangesAsync(ct);
	}
}
