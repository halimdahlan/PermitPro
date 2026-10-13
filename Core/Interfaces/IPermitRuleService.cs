using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Models.PermitRules;

namespace PermitPro.Core.Interfaces;

public interface IPermitRuleService
{
	// The company's preset rules, creating any that are missing (disabled, default settings).
	Task<List<PermitRule>> GetOrCreatePresetsAsync(Guid companyId, CancellationToken ct = default);

	// Validates and stores a preset's settings. Throws ArgumentException with a user-facing message.
	Task SaveAsync(Guid companyId, PermitRuleTypeEnum ruleType, bool isEnabled, string? settingsJson, CancellationToken ct = default);

	// Permits the rule would act on right now (settingsJson = unsaved settings from the page, or null for the stored ones).
	Task<PermitRulePreview> PreviewAsync(Guid companyId, PermitRuleTypeEnum ruleType, string? settingsJson, CancellationToken ct = default);

	// Runs every enabled rule of every active company once (PermitRulesBackgroundService).
	Task RunAllAsync(CancellationToken ct = default);
}
