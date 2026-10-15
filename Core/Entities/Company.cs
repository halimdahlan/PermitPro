using PermitPro.Core.Entities.Base;

namespace PermitPro.Core.Entities;

public class Company : EntityBase
{
	public required string Name { get; set; }

	public string? Description { get; set; }

	public bool IsActive { get; set; }

	public string? LogoFileName { get; set; }

	/// <summary>
	/// The company's own host name label, e.g. "iotm" for iotm.permitpro.app. Lowercase, unique;
	/// rules in <see cref="Helpers.CompanySubdomain"/>. Null = not reachable on a subdomain yet.
	/// </summary>
	public string? Subdomain { get; set; }

	public List<Address> CompanyAddresses { get; set; } = new();

	public List<UserInfo> CompanyUsers { get; set; } = new();

	public List<Site> CompanySites { get; set; } = new();

	public List<Workflow> CompanyWorkflows { get; set; } = new();

	public List<Permit> CompanyPermits { get; set; } = new();

}
