using PermitPro.Core.Entities.Base;
using PermitPro.Core.Enums;

namespace PermitPro.Core.Entities;

public class FormTemplate : EntityBase
{
    public Guid CompanyId { get; set; }

    public Company? Company { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public FormTemplateTypeEnum TemplateType { get; set; }

    // Rows/columns/control placements. Each row has columnCount (1-3)
    // and an array of column slots (each slot has controlId or null).
    public required string LayoutJson { get; set; }

    public string? HeaderConfigJson { get; set; }

    public string? FooterConfigJson { get; set; }
}
