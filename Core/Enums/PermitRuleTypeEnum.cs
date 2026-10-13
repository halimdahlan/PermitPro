
namespace PermitPro.Core.Enums;

// Preset permit rules (phase 1). Each company has at most one rule of each type.
public enum PermitRuleTypeEnum
{
	// End date has passed and the permit isn't closed/rejected: notify, then repeat every N days.
	Overdue = 1,

	// Closed, closed (no action) or rejected for N days: archive the permit.
	AutoArchive = 2,

	// Pending at the same workflow step for more than N hours: remind the approvers and escalate.
	PendingEscalation = 3,

	// End date within the next N hours: remind the creator.
	EndDateReminder = 4,
}
