using PermitPro.Core.Entities.Base;
using PermitPro.Core.Enums;

namespace PermitPro.Core.Entities;

// A company's preset permit rule, run by PermitRulesBackgroundService.
public class PermitRule : EntityBase
{
	public Guid CompanyId { get; set; }

	public Company? Company { get; set; }

	public PermitRuleTypeEnum RuleType { get; set; }

	public bool IsEnabled { get; set; }

	// Rule-type specific settings (Core/Models/PermitRules), as JSON.
	public string? SettingsJson { get; set; }

	public DateTime? LastRunWhen { get; set; }

	public string? LastRunSummary { get; set; }
}
