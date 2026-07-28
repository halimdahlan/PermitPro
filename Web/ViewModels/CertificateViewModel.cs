using PermitPro.Core.Enums;

namespace PermitPro.App.ViewModels;

public class CertificateListViewModel
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool HasTemplate { get; set; }
    public bool HasData { get; set; }
}

public class CertificateFormViewModel
{
    public Guid CertificateId { get; set; }
    public Guid PermitId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? FormTemplateId { get; set; }

    // Enriched layout for rendering (same pattern as EditPermitViewModel)
    public string LayoutJson { get; set; } = "{\"rows\":[]}";
    public string? CertificateDataJson { get; set; }

    // Available certificate templates for this company
    public List<FormTemplateListViewModel> AvailableTemplates { get; set; } = new();
}
