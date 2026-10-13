using PermitPro.Core.Enums;

namespace PermitPro.Core.Models.PermitRules;

// Settings of the Overdue rule (PermitRule.SettingsJson).
public class OverdueRuleSettings
{
	// Repeat the reminder every N days until the permit is closed.
	public int RepeatEveryDays { get; set; } = 1;

	public bool NotifyCreator { get; set; } = true;

	// Super Users and Portal Admins of the company.
	public bool NotifyAdmins { get; set; } = true;

	// Extra addresses, separated by commas, semicolons or new lines.
	public string? ExtraEmails { get; set; }

	// Drafts are left out unless this is on.
	public bool IncludeDrafts { get; set; }
}

// Settings of the Approval escalation rule (PermitRule.SettingsJson).
public class PendingEscalationRuleSettings
{
	// Pending at the same workflow step for more than this many hours.
	public int PendingHours { get; set; } = 24;

	// Repeat every N hours while the permit stays at that step.
	public int RepeatEveryHours { get; set; } = 24;

	// Approvers of the step the permit is waiting at (their own permits).
	public bool NotifyApprovers { get; set; } = true;

	// Super Users and Portal Admins of the company (all of them).
	public bool NotifyAdmins { get; set; } = true;

	public string? ExtraEmails { get; set; }
}

// Settings of the End-date reminder rule (PermitRule.SettingsJson).
public class EndDateReminderRuleSettings
{
	// Remind this many hours before the end date.
	public int HoursBefore { get; set; } = 24;

	public bool NotifyCreator { get; set; } = true;

	public string? ExtraEmails { get; set; }
}

// Settings of the Auto-archive rule (PermitRule.SettingsJson).
public class AutoArchiveRuleSettings
{
	// Archive closed / rejected permits this many days after they were closed.
	public int DaysAfterClosed { get; set; } = 30;
}

// Permits a rule would act on right now (Rules page dry run).
public class PermitRulePreview
{
	public int Count { get; set; }

	public List<PermitRulePreviewItem> Sample { get; set; } = [];
}

public class PermitRulePreviewItem
{
	public Guid Id { get; set; }

	public int RunningNumber { get; set; }

	public string PermitNo => $"PTW{RunningNumber:000000}";

	public string? Description { get; set; }

	public string Status { get; set; } = string.Empty;

	// End date (Overdue, End-date reminder), waiting since (Approval escalation) or closed date (Auto-archive), UTC.
	public DateTime? Date { get; set; }
}

// One run of a rule that did something (Rules → History), from the PERMIT_RULE audit log.
public class PermitRuleHistoryItem
{
	public DateTime When { get; set; }

	public PermitRuleTypeEnum? RuleType { get; set; }

	public bool IsError { get; set; }

	public string Message { get; set; } = string.Empty;

	public List<PermitRuleHistoryPermit> Permits { get; set; } = [];
}

public class PermitRuleHistoryPermit
{
	public Guid Id { get; set; }

	public string PermitNo { get; set; } = string.Empty;
}
