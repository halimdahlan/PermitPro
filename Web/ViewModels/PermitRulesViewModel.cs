using PermitPro.Core.Enums;

namespace PermitPro.App.ViewModels;

public class PermitRulesViewModel
{
	public Guid CompanyId { get; set; }

	public List<PermitRuleItemViewModel> Rules { get; set; } = [];

	// PermitRules:SendEmails; when false the Overdue rule only sends in-app notifications.
	public bool EmailsEnabled { get; set; }
}

public class PermitRuleItemViewModel
{
	public PermitRuleTypeEnum RuleType { get; set; }

	public bool IsEnabled { get; set; }

	public string SettingsJson { get; set; } = "{}";

	// Malaysia time.
	public DateTime? LastRunWhen { get; set; }

	public string? LastRunSummary { get; set; }
}
