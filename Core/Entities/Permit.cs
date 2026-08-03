using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

using System.ComponentModel.DataAnnotations;

namespace PermitPro.Core.Entities;

public class Permit : ISoftDeletable
{
	[Key]
	public Guid Id { get; set; }

	public DateTime CreatedWhen { get; set; }

	public DateTime? UpdatedWhen { get; set; }

	public Guid? CreatedBy { get; set; }

	public Guid? UpdatedBy { get; set; }

	public bool IsDeleted { get; set; }

	public DateTime? DeletedWhen { get; set; }

	public Guid? DeletedBy { get; set; }

	public required string PermitNo { get; set; }

	// Display columns used by both old and new-system permits for grid/list views.
	public string? Title { get; set; }

	//public Guid SiteId { get; set; }
	public Site? Site { get; set; } = null!;

	public string? Description { get; set; }

	public DateTime? StartDateTime { get; set; }

	public DateTime? EndDateTime { get; set; }

	public string? PermitHolderName { get; set; }

	public string? PermitHolderCompanyName { get; set; }

	public int PermitHolderNumOfStaff { get; set; }

	public PermitStatusEnum PermitStatus { get; set; }

	public PermitStatusEnum? PreviousPermitStatus { get; set; }

	public string? PermitForm { get; set; }

	// Filled field values for new-system permits (on/after cutover date).
	// Keyed by column UUID from FormTemplate.LayoutJson.
	public string? PermitDataJson { get; set; }

	public int RunningNumber { get; set; }

	public DateTime? SuspendedDateTime { get; set; }

	public DateTime? ResumedDateTime { get; set; }

	public DateTime? ApprovedDateTime { get; set; }

	public DateTime? RejectedDateTime { get; set; }

	public bool AutoResumeSuspended { get; set; }

	public string? Remarks { get; set; }

	public Workflow? PermitWorkflow { get; set; }

	public WorkflowStep? PermitWorkflowStep { get; set; }

	public Guid? CompanyId { get; set; }

	public Company? Company { get; set; }

	public List<Attachment> Attachments { get; set; } = new();

	public List<Certificate> Certificates { get; set; } = new();

	public List<WorkflowHistory> WorkflowHistories { get; set; } = new();

}
