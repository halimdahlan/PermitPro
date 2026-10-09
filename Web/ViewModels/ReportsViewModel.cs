using Microsoft.AspNetCore.Mvc.Rendering;

namespace PermitPro.App.ViewModels;

public class ReportsViewModel
{
	public Guid CompanyId { get; set; }

	// Options for the report filters. The first item of each list is the empty "(select)" option.
	public IList<SelectListItem> Months { get; set; } = new List<SelectListItem>();

	public IList<SelectListItem> Years { get; set; } = new List<SelectListItem>();

	public IList<SelectListItem> PermitHolders { get; set; } = new List<SelectListItem>();

	public IList<SelectListItem> Certificates { get; set; } = new List<SelectListItem>();

	public IList<SelectListItem> PermitStatuses { get; set; } = new List<SelectListItem>();
}
