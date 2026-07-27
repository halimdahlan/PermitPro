using PermitPro.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class FormControlsIndexViewModel
{
    public List<FormControlListViewModel> Controls { get; set; } = new();
    public string? Search { get; set; }
    public FormControlTypeEnum? TypeFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public Guid CompanyId { get; set; }
}

public class FormControlListViewModel
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Label { get; set; }
    public FormControlTypeEnum ControlType { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedWhen { get; set; }
}

public class FormControlFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [MaxLength(100, ErrorMessage = "Name must not exceed 100 characters")]
    [Display(Name = "Internal Name")]
    public required string Name { get; set; }

    [Required(ErrorMessage = "Label is required")]
    [MaxLength(200, ErrorMessage = "Label must not exceed 200 characters")]
    [Display(Name = "Label")]
    public required string Label { get; set; }

    [Required(ErrorMessage = "Control type is required")]
    [Display(Name = "Control Type")]
    public FormControlTypeEnum ControlType { get; set; }

    [Display(Name = "Options (JSON)")]
    public string? OptionsJson { get; set; }

    [Display(Name = "Required")]
    public bool IsRequired { get; set; }

    [MaxLength(200, ErrorMessage = "Placeholder must not exceed 200 characters")]
    [Display(Name = "Placeholder")]
    public string? Placeholder { get; set; }

    [MaxLength(500, ErrorMessage = "Help text must not exceed 500 characters")]
    [Display(Name = "Help Text")]
    public string? HelpText { get; set; }

    [Display(Name = "Sort Order")]
    public int SortOrder { get; set; }

    public bool IsEdit => Id.HasValue && Id != Guid.Empty;
}
