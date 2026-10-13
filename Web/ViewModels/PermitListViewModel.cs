using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class PermitListViewModel
{
	public Guid Id { get; set; }

	[Display(Name = "PERMIT NO.")]
	public string? PermitNumber { get; set; }

	[Display(Name = "DESCRIPTION")]
	public string? Description { get; set; }

	[Display(Name = "LOCATION")]
	public string? Location { get; set; }

	[Display(Name = "START DATE")]
	public DateTime? StartDate { get; set; }

	[Display(Name = "END DATE")]
	public DateTime? EndDate { get; set; }

	[Display(Name = "CERTIFICATES")]
	public string? Certificates { get; set; }

	[Display(Name = "STATUS")]
	public string? Status { get; set; }

	[Display(Name = "STATUS")]
	public string? StatusBadge { get; set; }

	[Display(Name = "DATE SUBMITTED")]
	public DateTime? DateSubmitted { get; set; }

	[Display(Name = "SUBMITTED BY")]
	public string? SubmittedBy { get; set; }

	public string? CompanyId { get; set; }

	public bool IsArchived { get; set; }
}
