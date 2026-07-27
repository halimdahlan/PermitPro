using PermitPro.Core.Entities.Base;
using PermitPro.Core.Enums;

namespace PermitPro.Core.Entities;

public class FormControl : EntityBase
{
    public Guid CompanyId { get; set; }

    public Company? Company { get; set; }

    public required string Name { get; set; }

    public required string Label { get; set; }

    public FormControlTypeEnum ControlType { get; set; }

    // JSON array of {value, label} for Select/MultiChoice.
    // For FileUpload: {maxFiles, allowedTypes}.
    public string? OptionsJson { get; set; }

    public bool IsRequired { get; set; }

    public string? Placeholder { get; set; }

    public string? HelpText { get; set; }

    public int SortOrder { get; set; }
}
