using PermitPro.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class FormTemplateListViewModel
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public FormTemplateTypeEnum TemplateType { get; set; }
    public DateTime CreatedWhen { get; set; }
    public DateTime? UpdatedWhen { get; set; }
}

public class FormTemplatesIndexViewModel
{
    public List<FormTemplateListViewModel> Templates { get; set; } = new();
    public FormTemplateTypeEnum? TypeFilter { get; set; }
    public Guid CompanyId { get; set; }
}

public class FormBuilderViewModel
{
    public Guid? TemplateId { get; set; }
    public Guid CompanyId { get; set; }

    [Required(ErrorMessage = "Template name is required")]
    [MaxLength(200)]
    public required string Name { get; set; }

    public string? Description { get; set; }
    public FormTemplateTypeEnum TemplateType { get; set; }
    public string LayoutJson { get; set; } = "{\"rows\":[]}";
    public string? HeaderConfigJson { get; set; }
    public string? FooterConfigJson { get; set; }

    // Controls available in the library panel (all controls for this company)
    public List<FormControlLibraryItem> ControlLibrary { get; set; } = new();

    public bool IsEdit => TemplateId.HasValue;
}

public class FormControlLibraryItem
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Label { get; set; }
    public FormControlTypeEnum ControlType { get; set; }
    public bool IsRequired { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public string? OptionsJson { get; set; }
}
