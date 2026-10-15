using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class AdminCompanyListViewModel
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int UserCount { get; set; }
    public int SiteCount { get; set; }
    public DateTime CreatedWhen { get; set; }
    public string? LogoFileName { get; set; }
    public string? Subdomain { get; set; }
    public string? SiteUrl { get; set; }
}

public class AdminCompanyFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Company name is required")]
    [MaxLength(200, ErrorMessage = "Name must not exceed 200 characters")]
    [Display(Name = "Company Name")]
    public required string Name { get; set; }

    [MaxLength(500, ErrorMessage = "Description must not exceed 500 characters")]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    // Format, reserved names and uniqueness are checked in AdminController (CompanySubdomain rules).
    [Required(ErrorMessage = "Subdomain is required")]
    [MaxLength(63, ErrorMessage = "Subdomain must not exceed 63 characters")]
    [Display(Name = "Subdomain")]
    public string? Subdomain { get; set; }

    // ".permitpro.app", shown after the subdomain box.
    public string HostSuffix { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Company Logo")]
    public IFormFile? LogoFile { get; set; }

    public string? ExistingLogoFileName { get; set; }

    public bool RemoveLogo { get; set; }

    public bool IsEdit => Id.HasValue && Id != Guid.Empty;
}
