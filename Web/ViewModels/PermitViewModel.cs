#nullable disable

using Microsoft.AspNetCore.Mvc.Rendering;

namespace PermitPro.App.ViewModels;

public class PermitViewModel
{
	public Guid CompanyId { get; set; }
	public IList<string> UserRoles { get; set; }
	public EditPermitViewModel EditPermitViewModel { get; set; }
	public string GridFilter { get; set; }
	public string GridFilterField { get; set; }
	public IList<SelectListItem> StatusOptions { get; set; } = new List<SelectListItem>();
	public bool PermitTemplateMissing { get; set; }
	public bool ShowPermitTemplateMissing { get; set; }
}
