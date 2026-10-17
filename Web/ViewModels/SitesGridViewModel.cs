#nullable disable

using PermitPro.Core.Enums;

using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class SitesGridViewModel
{
	public Guid Id { get; set; }

	public Guid ParentId { get; set; }

	[Display(Name = "NAME")]
	public string Name { get; set; }

	[Display(Name = "DESCRIPTION")]
	public string Description { get; set; }

	[Display(Name = "CONTACT NAME")]
	public string ContactName { get; set; }

	[Display(Name = "CONTACT EMAIL")]
	public string ContactEmail { get; set; }

	/// <summary>Permits assigned directly to this location.</summary>
	[Display(Name = "NUM. OF PERMITS")]
	public int PermitCount { get; set; }

	/// <summary>Permits at this location and every location under it.</summary>
	public int PermitCountWithSubLocations { get; set; }

	public bool HasSubLocations { get; set; }

	[Display(Name = "IS ACTIVE?")]
	public bool IsActive { get; set; }

	public SiteTypeEnum SiteType { get; set; }

	public string ActionIcons { get; set; }

}
