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

// Preset permit rules (End-date reminder, Overdue, Approval escalation, Auto-archive). RunAllAsync is
// called every few minutes by PermitRulesBackgroundService; there is no HTTP request, so email links
// come from App:BaseUrl and in-app notification links are relative.
public class PermitRuleService : IPermitRuleService
{
	private static readonly PermitStatusEnum[] _closedStatuses =
		[PermitStatusEnum.Closed, PermitStatusEnum.ClosedNoAction, PermitStatusEnum.Rejected];

	private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

	private const int PreviewSampleSize = 10;
	private const string AuditCategory = "PERMIT_RULE";

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
		PermitRuleTypeEnum.PendingEscalation => JsonSerializer.Serialize(new PendingEscalationRuleSettings()),
		PermitRuleTypeEnum.EndDateReminder => JsonSerializer.Serialize(new EndDateReminderRuleSettings()),
		_ => "{}",
	};

	private static string NormalizeSettings(PermitRuleTypeEnum type, string? settingsJson)
	{
		switch (type)
		{
			case PermitRuleTypeEnum.Overdue:
				var overdue = Parse<OverdueRuleSettings>(settingsJson);
				if (overdue.RepeatEveryDays is < 1 or > 365)
					throw new ArgumentException("Remind again every must be between 1 and 365 days.");
				overdue.ExtraEmails = NormalizeEmails(overdue.ExtraEmails);
				return JsonSerializer.Serialize(overdue);

			case PermitRuleTypeEnum.PendingEscalation:
				var pending = Parse<PendingEscalationRuleSettings>(settingsJson);
				if (pending.PendingHours is < 1 or > 2160)
					throw new ArgumentException("Pending for more than must be between 1 and 2160 hours (90 days).");
				if (pending.RepeatEveryHours is < 1 or > 2160)
					throw new ArgumentException("Remind again every must be between 1 and 2160 hours (90 days).");
				pending.ExtraEmails = NormalizeEmails(pending.ExtraEmails);
				return JsonSerializer.Serialize(pending);

			case PermitRuleTypeEnum.EndDateReminder:
				var reminder = Parse<EndDateReminderRuleSettings>(settingsJson);
				if (reminder.HoursBefore is < 1 or > 720)
					throw new ArgumentException("Remind before the end date must be between 1 and 720 hours (30 days).");
				reminder.ExtraEmails = NormalizeEmails(reminder.ExtraEmails);
				return JsonSerializer.Serialize(reminder);

			case PermitRuleTypeEnum.AutoArchive:
				var archive = Parse<AutoArchiveRuleSettings>(settingsJson);
				if (archive.DaysAfterClosed is < 1 or > 3650)
					throw new ArgumentException("Days after closing must be between 1 and 3650.");
				return JsonSerializer.Serialize(archive);

			default:
				throw new ArgumentException("Unknown rule.");
		}
	}

	private static string NormalizeEmails(string? emails)
	{
		var list = SplitEmails(emails);
		var invalid = list.Where(e => !IsValidEmail(e)).ToList();
		if (invalid.Count > 0)
			throw new ArgumentException($"Not a valid email address: {string.Join(", ", invalid)}");
		return string.Join("; ", list);
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

	// Member-init class (not a positional record) so EF can keep filtering and sorting on it.
	private sealed class RulePermit
	{
		public Guid Id { get; init; }
		public int RunningNumber { get; init; }
		public string? Description { get; init; }
		public string? Location { get; init; }
		public PermitStatusEnum Status { get; init; }
		public Guid? CreatedBy { get; init; }
		public Guid? WorkflowStepId { get; init; }
		public string? WorkflowStepName { get; init; }
		// End date (reminder, overdue), waiting since (escalation) or closed date (archive), UTC.
		public DateTime Date { get; init; }
	}

	private IQueryable<Permit> Permits(Guid companyId) =>
		_db.Permits.AsNoTracking().Where(p => p.CompanyId == companyId && !p.IsArchived);

	// End date within the next N hours; not Draft, Closed, Closed – no action or Rejected.
	private IQueryable<RulePermit> EndingSoonQuery(Guid companyId, DateTime nowUtc, int hoursBefore)
	{
		var until = nowUtc.AddHours(hoursBefore);
		return Permits(companyId)
			.Where(p => p.EndDateTime != null && p.EndDateTime > nowUtc && p.EndDateTime <= until &&
				p.PermitStatus != PermitStatusEnum.Draft && !_closedStatuses.Contains(p.PermitStatus))
			.Select(p => new RulePermit
			{
				Id = p.Id,
				RunningNumber = p.RunningNumber,
				Description = p.Description,
				Location = p.Site != null ? p.Site.Name : null,
				Status = p.PermitStatus,
				CreatedBy = p.CreatedBy,
				Date = p.EndDateTime!.Value,
			});
	}

	// End date passed and not closed / rejected (the definition Reports uses); drafts only if asked.
	private IQueryable<RulePermit> OverdueQuery(Guid companyId, DateTime nowUtc, bool includeDrafts) =>
		Permits(companyId)
			.Where(p => p.EndDateTime != null && p.EndDateTime < nowUtc && !_closedStatuses.Contains(p.PermitStatus) &&
				(includeDrafts || p.PermitStatus != PermitStatusEnum.Draft))
			.Select(p => new RulePermit
			{
				Id = p.Id,
				RunningNumber = p.RunningNumber,
				Description = p.Description,
				Location = p.Site != null ? p.Site.Name : null,
				Status = p.PermitStatus,
				CreatedBy = p.CreatedBy,
				Date = p.EndDateTime!.Value,
			});

	// Pending, waiting at its current workflow step since the latest workflow history entry
	// (moving to the next step restarts the clock), for more than N hours.
	private IQueryable<RulePermit> PendingQuery(Guid companyId, DateTime nowUtc, int pendingHours)
	{
		var since = nowUtc.AddHours(-pendingHours);
		return Permits(companyId)
			.Where(p => p.PermitStatus == PermitStatusEnum.Pending)
			.Select(p => new RulePermit
			{
				Id = p.Id,
				RunningNumber = p.RunningNumber,
				Description = p.Description,
				Location = p.Site != null ? p.Site.Name : null,
				Status = p.PermitStatus,
				CreatedBy = p.CreatedBy,
				WorkflowStepId = p.PermitWorkflowStep != null ? p.PermitWorkflowStep.Id : null,
				WorkflowStepName = p.PermitWorkflowStep != null ? p.PermitWorkflowStep.Name : null,
				Date = p.WorkflowHistories.Max(h => (DateTime?)h.CreatedWhen) ?? p.UpdatedWhen ?? p.CreatedWhen,
			})
			.Where(p => p.Date <= since);
	}

	// Closed, closed (no action) or rejected for N days, with the date it was closed: rejection date,
	// else the latest Closed/Rejected workflow history entry, else the last update. Permits the rule
	// has acted on before (archived, then unarchived by a Super User) are left alone.
	private IQueryable<RulePermit> ClosedQuery(Guid companyId, Guid? archiveRuleId, DateTime nowUtc, int daysAfterClosed)
	{
		var cutoff = nowUtc.AddDays(-daysAfterClosed);
		return Permits(companyId)
			.Where(p => _closedStatuses.Contains(p.PermitStatus) &&
				!_db.PermitRuleRuns.Any(r => r.PermitRuleId == archiveRuleId && r.PermitId == p.Id))
			.Select(p => new RulePermit
			{
				Id = p.Id,
				RunningNumber = p.RunningNumber,
				Description = p.Description,
				Status = p.PermitStatus,
				Date = (p.PermitStatus == PermitStatusEnum.Rejected ? p.RejectedDateTime : null)
					?? p.WorkflowHistories
						.Where(h => h.Status == WorkflowStatusEnum.Closed || h.Status == WorkflowStatusEnum.Rejected)
						.Max(h => (DateTime?)h.CreatedWhen)
					?? p.UpdatedWhen
					?? p.CreatedWhen,
			})
			.Where(p => p.Date <= cutoff);
	}

	private IQueryable<RulePermit> CandidatesQuery(PermitRule rule, string? settingsJson, DateTime nowUtc) => rule.RuleType switch
	{
		PermitRuleTypeEnum.EndDateReminder => EndingSoonQuery(rule.CompanyId, nowUtc, Parse<EndDateReminderRuleSettings>(settingsJson).HoursBefore),
		PermitRuleTypeEnum.Overdue => OverdueQuery(rule.CompanyId, nowUtc, Parse<OverdueRuleSettings>(settingsJson).IncludeDrafts),
		PermitRuleTypeEnum.PendingEscalation => PendingQuery(rule.CompanyId, nowUtc, Parse<PendingEscalationRuleSettings>(settingsJson).PendingHours),
		PermitRuleTypeEnum.AutoArchive => ClosedQuery(rule.CompanyId, rule.Id, nowUtc, Parse<AutoArchiveRuleSettings>(settingsJson).DaysAfterClosed),
		_ => throw new ArgumentException("Unknown rule."),
	};

	public async Task<PermitRulePreview> PreviewAsync(Guid companyId, PermitRuleTypeEnum ruleType, string? settingsJson, CancellationToken ct = default)
	{
		var rule = (await GetOrCreatePresetsAsync(companyId, ct)).Single(r => r.RuleType == ruleType);
		var query = CandidatesQuery(rule, settingsJson ?? rule.SettingsJson, DateTime.UtcNow);

		return new PermitRulePreview
		{
			Count = await query.CountAsync(ct),
			Sample = await query
				.OrderBy(p => p.Date)
				.Take(PreviewSampleSize)
				.Select(p => new PermitRulePreviewItem
				{
					Id = p.Id,
					RunningNumber = p.RunningNumber,
					Description = p.Description,
					Status = p.Status.ToString(),
					Date = p.Date,
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
						PermitRuleTypeEnum.EndDateReminder => await RunEndDateReminderAsync(rule, now, ct),
						PermitRuleTypeEnum.Overdue => await RunOverdueAsync(rule, now, ct),
						PermitRuleTypeEnum.PendingEscalation => await RunPendingEscalationAsync(rule, now, ct),
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
					await AddAuditLogAsync(rule.CompanyId, rule.RuleType, LogTypeEnum.Error, $"{RuleName(rule.RuleType)} rule failed: {ex.Message}", [], ct);
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

	public static string RuleName(PermitRuleTypeEnum type) => type switch
	{
		PermitRuleTypeEnum.EndDateReminder => "End-date reminder",
		PermitRuleTypeEnum.Overdue => "Overdue permits",
		PermitRuleTypeEnum.PendingEscalation => "Approval escalation",
		PermitRuleTypeEnum.AutoArchive => "Auto-archive",
		_ => type.ToString(),
	};

	private static string PermitNo(int runningNumber) => $"PTW{runningNumber:000000}";

	private static string Plural(int n, string one, string many) => n == 1 ? one : many;

	private static string Hours(int hours) =>
		hours % 24 == 0 ? $"{hours / 24} {Plural(hours / 24, "day", "days")}" : $"{hours} {Plural(hours, "hour", "hours")}";

	private static string Duration(TimeSpan span) =>
		span.TotalHours < 1 ? "under an hour"
		: span.TotalHours < 48 ? $"{(int)span.TotalHours} {Plural((int)span.TotalHours, "hour", "hours")}"
		: $"{(int)span.TotalDays} days";

	private string BaseUrl() =>
		(_configuration["App:BaseUrl"] ?? (_env.IsDevelopment() ? "https://localhost:7079" : "https://permitpro.app")).TrimEnd('/');


	// ── End-date reminder ─────────────────────────────────────────────────────

	private async Task<string> RunEndDateReminderAsync(PermitRule rule, DateTime now, CancellationToken ct)
	{
		var settings = Parse<EndDateReminderRuleSettings>(rule.SettingsJson);
		var ending = await EndingSoonQuery(rule.CompanyId, now, settings.HoursBefore).ToListAsync(ct);
		if (ending.Count == 0)
			return "No permits ending soon.";

		// Once per permit; again only if the end date moved later after the last reminder.
		var runs = await RunsAsync(rule, ct);
		var due = ending
			.Where(p => !runs.TryGetValue(p.Id, out var run) || run.LastRunWhen < p.Date.AddHours(-settings.HoursBefore))
			.OrderBy(p => p.Date)
			.ToList();
		if (due.Count == 0)
			return $"{ending.Count} ending soon, all already reminded.";

		var recipients = new Recipients();
		if (settings.NotifyCreator)
			await recipients.AddCreatorsAsync(_db, due, ct);
		recipients.AddExtra(SplitEmails(settings.ExtraEmails), due);

		var sent = await SendDigestsAsync(rule.CompanyId, recipients.List, new Notice
		{
			Title = "Permits ending soon",
			SingleSubject = no => $"Permit ending soon: {no}",
			ManySubject = n => $"{n} permits end soon",
			SingleMessage = no => $"{no} ends within {Hours(settings.HoursBefore)}.",
			ManyMessage = (n, list) => $"{n} permits end within {Hours(settings.HoursBefore)}: {list}.",
			Intro = n => n == 1
				? $"The following permit ends within the next {Hours(settings.HoursBefore)}. Please close it on time or extend its end date."
				: $"The following {n} permits end within the next {Hours(settings.HoursBefore)}. Please close them on time or extend their end date.",
			DateLabel = "End date",
			ExtraLabel = "Ends in",
			Extra = p => Duration(p.Date - now),
		}, ct);

		await RecordRunsAsync(rule, runs, due, now, ct);
		return await LogNoticeAsync(rule, due, sent, "Reminded about", "permit ending soon", "permits ending soon", ct);
	}


	// ── Overdue ───────────────────────────────────────────────────────────────

	private async Task<string> RunOverdueAsync(PermitRule rule, DateTime now, CancellationToken ct)
	{
		var settings = Parse<OverdueRuleSettings>(rule.SettingsJson);
		var overdue = await OverdueQuery(rule.CompanyId, now, settings.IncludeDrafts).ToListAsync(ct);
		if (overdue.Count == 0)
			return "No overdue permits.";

		// Notify once, then again every N days, per permit.
		var runs = await RunsAsync(rule, ct);
		var repeatBefore = now.AddDays(-settings.RepeatEveryDays);
		var due = overdue
			.Where(p => !runs.TryGetValue(p.Id, out var run) || run.LastRunWhen <= repeatBefore)
			.OrderBy(p => p.Date)
			.ToList();
		if (due.Count == 0)
			return $"{overdue.Count} overdue, none due for a reminder.";

		var recipients = new Recipients();
		if (settings.NotifyCreator)
			await recipients.AddCreatorsAsync(_db, due, ct);
		if (settings.NotifyAdmins)
			await recipients.AddAdminsAsync(_db, rule.CompanyId, due, ct);
		recipients.AddExtra(SplitEmails(settings.ExtraEmails), due);

		var again = settings.RepeatEveryDays == 1 ? "tomorrow" : $"in {settings.RepeatEveryDays} days";
		var sent = await SendDigestsAsync(rule.CompanyId, recipients.List, new Notice
		{
			Title = "Overdue permits",
			SingleSubject = no => $"Permit overdue: {no}",
			ManySubject = n => $"{n} permits are overdue",
			SingleMessage = no => $"{no} is overdue.",
			ManyMessage = (n, list) => $"{n} permits are overdue: {list}.",
			Intro = n => n == 1
				? "The following permit has passed its end date and is not closed yet. Please close it or update the end date."
				: $"The following {n} permits have passed their end date and are not closed yet. Please close them or update the end date.",
			DateLabel = "End date",
			ExtraLabel = "Overdue",
			Extra = p => (now - p.Date).TotalDays < 1 ? "today" : $"{(int)(now - p.Date).TotalDays} {Plural((int)(now - p.Date).TotalDays, "day", "days")}",
			Footer = $"You'll be reminded again {again} while a permit stays overdue.",
		}, ct);

		await RecordRunsAsync(rule, runs, due, now, ct);
		return await LogNoticeAsync(rule, due, sent, "Reminded about", "overdue permit", "overdue permits", ct);
	}


	// ── Approval escalation ───────────────────────────────────────────────────

	private async Task<string> RunPendingEscalationAsync(PermitRule rule, DateTime now, CancellationToken ct)
	{
		var settings = Parse<PendingEscalationRuleSettings>(rule.SettingsJson);
		var waiting = await PendingQuery(rule.CompanyId, now, settings.PendingHours).ToListAsync(ct);
		if (waiting.Count == 0)
			return "No permits waiting too long for approval.";

		// Notify when it has waited too long, then every N hours; moving to another step starts over.
		var runs = await RunsAsync(rule, ct);
		var repeatBefore = now.AddHours(-settings.RepeatEveryHours);
		var due = waiting
			.Where(p => !runs.TryGetValue(p.Id, out var run) || run.LastRunWhen < p.Date || run.LastRunWhen <= repeatBefore)
			.OrderBy(p => p.Date)
			.ToList();
		if (due.Count == 0)
			return $"{waiting.Count} waiting for approval, none due for a reminder.";

		var recipients = new Recipients();
		if (settings.NotifyApprovers)
			await recipients.AddApproversAsync(_db, due, ct);
		if (settings.NotifyAdmins)
			await recipients.AddAdminsAsync(_db, rule.CompanyId, due, ct);
		recipients.AddExtra(SplitEmails(settings.ExtraEmails), due);

		var sent = await SendDigestsAsync(rule.CompanyId, recipients.List, new Notice
		{
			Title = "Approval overdue",
			SingleSubject = no => $"Waiting for approval: {no}",
			ManySubject = n => $"{n} permits are waiting for approval",
			SingleMessage = no => $"{no} has been waiting for approval for more than {Hours(settings.PendingHours)}.",
			ManyMessage = (n, list) => $"{n} permits have been waiting for approval for more than {Hours(settings.PendingHours)}: {list}.",
			Intro = n => n == 1
				? $"The following permit has been waiting for approval for more than {Hours(settings.PendingHours)}."
				: $"The following {n} permits have been waiting for approval for more than {Hours(settings.PendingHours)}.",
			DateLabel = "Waiting since",
			ExtraLabel = "Step",
			Extra = p => p.WorkflowStepName ?? "",
			Footer = $"You'll be reminded again in {Hours(settings.RepeatEveryHours)} while a permit keeps waiting at the same step.",
		}, ct);

		await RecordRunsAsync(rule, runs, due, now, ct);
		return await LogNoticeAsync(rule, due, sent, "Escalated", "permit waiting for approval", "permits waiting for approval", ct);
	}


	// ── Auto-archive ──────────────────────────────────────────────────────────

	private async Task<string> RunAutoArchiveAsync(PermitRule rule, DateTime now, CancellationToken ct)
	{
		var settings = Parse<AutoArchiveRuleSettings>(rule.SettingsJson);

		var toArchive = await ClosedQuery(rule.CompanyId, rule.Id, now, settings.DaysAfterClosed)
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

		// Remember which permits the rule archived, so an unarchived one isn't archived again.
		_db.PermitRuleRuns.AddRange(toArchive.Select(p => new PermitRuleRun
		{
			Id = Guid.NewGuid(),
			PermitRuleId = rule.Id,
			PermitId = p.Id,
			FirstRunWhen = now,
			LastRunWhen = now,
			RunCount = 1,
		}));
		await _db.SaveChangesAsync(ct);

		var summary = $"Archived {toArchive.Count} {Plural(toArchive.Count, "permit", "permits")}.";
		var list = string.Join(", ", toArchive.Take(20).Select(p => PermitNo(p.RunningNumber))) + (toArchive.Count > 20 ? $" and {toArchive.Count - 20} more" : "");
		await AddAuditLogAsync(rule.CompanyId, rule.RuleType, LogTypeEnum.Information,
			$"Auto-archive rule (closed {settings.DaysAfterClosed}+ days): {summary} Permits: {list}.",
			toArchive.Select(p => (p.Id, p.RunningNumber)), ct);

		return summary;
	}


	// ── Unarchive and history ─────────────────────────────────────────────────

	public async Task<bool> UnarchiveAsync(Guid companyId, Guid permitId, string byName, CancellationToken ct = default)
	{
		var permit = await _db.Permits.AsNoTracking()
			.Where(p => p.Id == permitId && p.CompanyId == companyId && p.IsArchived)
			.Select(p => new { p.Id, p.RunningNumber })
			.FirstOrDefaultAsync(ct);

		if (permit == null) return false;

		await _db.Permits
			.Where(p => p.Id == permitId)
			.ExecuteUpdateAsync(s => s
				.SetProperty(p => p.IsArchived, false)
				.SetProperty(p => p.ArchivedWhen, (DateTime?)null), ct);

		// Auto-archive skips permits it has a run for: keep one so this permit stays unarchived.
		var archiveRule = (await GetOrCreatePresetsAsync(companyId, ct)).Single(r => r.RuleType == PermitRuleTypeEnum.AutoArchive);
		if (!await _db.PermitRuleRuns.AnyAsync(r => r.PermitRuleId == archiveRule.Id && r.PermitId == permitId, ct))
		{
			var now = DateTime.UtcNow;
			_db.PermitRuleRuns.Add(new PermitRuleRun
			{
				Id = Guid.NewGuid(),
				PermitRuleId = archiveRule.Id,
				PermitId = permitId,
				FirstRunWhen = now,
				LastRunWhen = now,
				RunCount = 1,
			});
			await _db.SaveChangesAsync(ct);
		}

		await AddAuditLogAsync(companyId, PermitRuleTypeEnum.AutoArchive, LogTypeEnum.Information,
			$"{PermitNo(permit.RunningNumber)} was unarchived by {byName}. Auto-archive won't archive it again.",
			[(permit.Id, permit.RunningNumber)], ct);

		return true;
	}

	private sealed class AuditData
	{
		public PermitRuleTypeEnum? Rule { get; set; }
		public List<PermitRuleHistoryPermit> Permits { get; set; } = [];
	}

	public async Task<List<PermitRuleHistoryItem>> GetHistoryAsync(Guid companyId, int take = 500, CancellationToken ct = default)
	{
		var logs = await _db.AuditLogs.AsNoTracking()
			.Where(l => l.CompanyId == companyId && l.Category == AuditCategory)
			.OrderByDescending(l => l.CreatedWhen)
			.Take(take)
			.Select(l => new { l.CreatedWhen, l.LogType, l.Description, l.SerializedData })
			.ToListAsync(ct);

		return logs.Select(l =>
		{
			var data = Parse<AuditData>(l.SerializedData);
			// Entries written before SerializedData was stored: recognise the rule from the message.
			var ruleType = data.Rule ?? Enum.GetValues<PermitRuleTypeEnum>()
				.Cast<PermitRuleTypeEnum?>()
				.FirstOrDefault(t => (l.Description ?? "").StartsWith(RuleName(t!.Value), StringComparison.OrdinalIgnoreCase));
			return new PermitRuleHistoryItem
			{
				When = l.CreatedWhen,
				RuleType = ruleType,
				IsError = l.LogType == LogTypeEnum.Error,
				Message = l.Description ?? string.Empty,
				Permits = data.Permits,
			};
		}).ToList();
	}


	// ── Recipients and digests ────────────────────────────────────────────────

	private async Task<Dictionary<Guid, PermitRuleRun>> RunsAsync(PermitRule rule, CancellationToken ct) =>
		await _db.PermitRuleRuns.Where(r => r.PermitRuleId == rule.Id).ToDictionaryAsync(r => r.PermitId, ct);

	private async Task RecordRunsAsync(PermitRule rule, Dictionary<Guid, PermitRuleRun> runs, List<RulePermit> due, DateTime now, CancellationToken ct)
	{
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
	}

	private async Task<string> LogNoticeAsync(PermitRule rule, List<RulePermit> due, (int Recipients, bool Emails) sent,
		string verb, string one, string many, CancellationToken ct)
	{
		var summary = $"{verb} {due.Count} {Plural(due.Count, one, many)} ({sent.Recipients} {Plural(sent.Recipients, "recipient", "recipients")}{(sent.Emails ? "" : "; emails off, in-app only")}).";
		var list = string.Join(", ", due.Take(20).Select(p => PermitNo(p.RunningNumber))) + (due.Count > 20 ? $" and {due.Count - 20} more" : "");
		await AddAuditLogAsync(rule.CompanyId, rule.RuleType, LogTypeEnum.Information,
			$"{RuleName(rule.RuleType)} rule: {summary} Permits: {list}.",
			due.Select(p => (p.Id, p.RunningNumber)), ct);
		return summary;
	}

	private sealed class Recipient
	{
		public required string Name { get; init; }
		public required string Email { get; init; }
		public string? UserId { get; init; }
		public List<RulePermit> Permits { get; } = [];
	}

	// Collects recipients by email address; each gets their permits once.
	private sealed class Recipients
	{
		private readonly Dictionary<string, Recipient> _byEmail = new(StringComparer.OrdinalIgnoreCase);

		public List<Recipient> List => _byEmail.Values.Where(r => r.Permits.Count > 0).ToList();

		private void Add(string name, string email, string? userId, IEnumerable<RulePermit> permits)
		{
			if (!_byEmail.TryGetValue(email, out var recipient))
			{
				recipient = new Recipient { Name = name, Email = email, UserId = userId };
				_byEmail[email] = recipient;
			}
			foreach (var permit in permits)
				if (!recipient.Permits.Contains(permit))
					recipient.Permits.Add(permit);
		}

		// Permit creators, each with their own permits.
		public async Task AddCreatorsAsync(ApplicationDbContext db, List<RulePermit> due, CancellationToken ct)
		{
			var ids = due.Where(p => p.CreatedBy.HasValue).Select(p => p.CreatedBy!.Value.ToString().ToLower()).Distinct().ToList();
			var users = await db.Users.AsNoTracking()
				.Where(u => u.IsActive && u.Email != null && ids.Contains(u.Id.ToLower()))
				.Select(u => new { u.Id, u.Email, Name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() })
				.ToListAsync(ct);

			foreach (var user in users)
				Add(user.Name, user.Email!, user.Id,
					due.Where(p => p.CreatedBy.HasValue && p.CreatedBy.Value.ToString().Equals(user.Id, StringComparison.OrdinalIgnoreCase)));
		}

		// The company's Super Users and Portal Admins, with all the permits.
		public async Task AddAdminsAsync(ApplicationDbContext db, Guid companyId, List<RulePermit> due, CancellationToken ct)
		{
			var users = await db.Users.AsNoTracking()
				.Where(u => u.IsActive && u.Email != null && u.UserCompany != null && u.UserCompany.Id == companyId &&
					u.UserRoles.Any(ur => ur.Role!.NormalizedName == "SUPERUSER" || ur.Role!.NormalizedName == "PORTALADMIN"))
				.Select(u => new { u.Id, u.Email, Name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() })
				.ToListAsync(ct);

			foreach (var user in users)
				Add(user.Name, user.Email!, user.Id, due);
		}

		// Approvers of the step each permit is waiting at, with the permits waiting on them.
		public async Task AddApproversAsync(ApplicationDbContext db, List<RulePermit> due, CancellationToken ct)
		{
			var stepIds = due.Where(p => p.WorkflowStepId.HasValue).Select(p => p.WorkflowStepId!.Value).Distinct().ToList();
			var steps = await db.WorkflowSteps.AsNoTracking()
				.Where(s => stepIds.Contains(s.Id))
				.Select(s => new
				{
					s.Id,
					Approvers = s.Approvers
						.Where(u => u.IsActive && u.Email != null)
						.Select(u => new { u.Id, u.Email, Name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() }),
				})
				.ToListAsync(ct);

			foreach (var step in steps)
				foreach (var user in step.Approvers)
					Add(user.Name, user.Email!, user.Id, due.Where(p => p.WorkflowStepId == step.Id));
		}

		public void AddExtra(IEnumerable<string> emails, List<RulePermit> due)
		{
			foreach (var email in emails)
				Add(email, email, null, due);
		}
	}

	// Wording of one rule's digest (email and in-app notification).
	private sealed class Notice
	{
		public required string Title { get; init; }
		public required Func<string, string> SingleSubject { get; init; }
		public required Func<int, string> ManySubject { get; init; }
		public required Func<string, string> SingleMessage { get; init; }
		public required Func<int, string, string> ManyMessage { get; init; }
		public required Func<int, string> Intro { get; init; }
		public required string DateLabel { get; init; }
		public required string ExtraLabel { get; init; }
		public required Func<RulePermit, string> Extra { get; init; }
		public string? Footer { get; init; }
	}

	// One email (if PermitRules:SendEmails) and one in-app notification per recipient.
	private async Task<(int Recipients, bool Emails)> SendDigestsAsync(Guid companyId, List<Recipient> recipients, Notice notice, CancellationToken ct)
	{
		// PermitRules:SendEmails (default false): until it's turned on, only in-app notifications go out.
		var sendEmails = _configuration.GetValue("PermitRules:SendEmails", false);
		if (recipients.Count == 0)
			return (0, sendEmails);

		var companyName = await _db.Companies.AsNoTracking()
			.Where(c => c.Id == companyId)
			.Select(c => c.Description ?? c.Name)
			.FirstOrDefaultAsync(ct);

		var template = sendEmails
			? Template.Parse(await File.ReadAllTextAsync(Path.Combine(_env.WebRootPath, "templates", "html", "permit-rule.html"), ct))
			: null;
		var baseUrl = BaseUrl();

		foreach (var recipient in recipients)
		{
			var permits = recipient.Permits.OrderBy(p => p.Date).ToList();
			var numbers = permits.Select(p => PermitNo(p.RunningNumber)).ToList();

			if (template != null)
			{
				var body = template.Render(new
				{
					RecipientName = recipient.Name,
					CompanyName = companyName,
					Intro = notice.Intro(permits.Count),
					DateLabel = notice.DateLabel,
					ExtraLabel = notice.ExtraLabel,
					Footer = notice.Footer,
					PermitsLink = $"{baseUrl}/{companyId}/permits",
					Permits = permits.Select(p => new
					{
						PermitNo = PermitNo(p.RunningNumber),
						Description = p.Description ?? string.Empty,
						Location = p.Location ?? string.Empty,
						Date = GeneralHelper.GetDateInTimeZone(p.Date).ToString("dd MMM yyyy, hh:mm tt"),
						Extra = notice.Extra(p),
						Link = $"{baseUrl}/account/login?company={companyId}&entity=permits&id={p.Id}&origin=email",
					}),
				});

				var subject = permits.Count == 1 ? notice.SingleSubject(numbers[0]) : notice.ManySubject(permits.Count);

				// Errors are caught and written to the audit log by MessageService.
				await _messageService.SendEmailAsync(new EmailInfo { Name = recipient.Name, Email = recipient.Email, Subject = subject, Body = body }, companyId);
			}

			if (recipient.UserId == null) continue;

			var list = string.Join(", ", numbers.Take(5)) + (numbers.Count > 5 ? $" and {numbers.Count - 5} more" : "");
			var message = numbers.Count == 1 ? notice.SingleMessage(numbers[0]) : notice.ManyMessage(numbers.Count, list);

			_db.Notifications.Add(new Notification
			{
				Title = notice.Title,
				Message = message,
				// Relative, so it works on whichever host the user is on.
				Url = numbers.Count == 1 ? $"/{companyId}/permits/edit/{permits[0].Id}" : $"/{companyId}/permits",
				NotificationUser = await _db.Users.FindAsync([recipient.UserId], ct),
			});

			await _pushService.PushAsync(recipient.UserId, notice.Title, message);
		}

		await _db.SaveChangesAsync(ct);
		return (recipients.Count, sendEmails);
	}


	// History reads the rule and permits from SerializedData.
	private async Task AddAuditLogAsync(Guid companyId, PermitRuleTypeEnum ruleType, LogTypeEnum type, string message,
		IEnumerable<(Guid Id, int RunningNumber)> permits, CancellationToken ct)
	{
		var data = new AuditData
		{
			Rule = ruleType,
			Permits = permits.Select(p => new PermitRuleHistoryPermit { Id = p.Id, PermitNo = PermitNo(p.RunningNumber) }).ToList(),
		};

		_db.AuditLogs.Add(new AuditLog
		{
			Id = Guid.NewGuid(),
			LogType = type,
			Category = AuditCategory,
			EntityName = "PermitRule",
			Description = message,
			SerializedData = JsonSerializer.Serialize(data),
			CompanyId = companyId,
			CreatedWhen = DateTime.UtcNow,
		});
		await _db.SaveChangesAsync(ct);
	}
}
