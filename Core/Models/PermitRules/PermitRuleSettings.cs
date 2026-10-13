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

	// End date (Overdue) or closed date (Auto-archive), UTC.
	public DateTime? Date { get; set; }
}
