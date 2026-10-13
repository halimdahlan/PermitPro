using System.ComponentModel.DataAnnotations;

namespace PermitPro.Core.Entities;

// One row per rule and permit it has acted on, so a rule doesn't repeat itself by accident
// (the Overdue rule uses LastRunWhen to repeat its reminder every N days).
public class PermitRuleRun
{
	[Key]
	public Guid Id { get; set; }

	public Guid PermitRuleId { get; set; }

	public PermitRule? PermitRule { get; set; }

	public Guid PermitId { get; set; }

	public Permit? Permit { get; set; }

	public DateTime FirstRunWhen { get; set; }

	public DateTime LastRunWhen { get; set; }

	public int RunCount { get; set; }
}
