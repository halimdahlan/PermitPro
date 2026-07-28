using PermitPro.Core.Entities.Base;

namespace PermitPro.Core.Entities;

public class Certificate : EntityBase
{
	public required string Name { get; set; }

	public string? Description { get; set; }

	public string? SerializedData { get; set; }

	// New-system: filled field values keyed by column UUID from the linked FormTemplate.LayoutJson
	public string? CertificateDataJson { get; set; }

	// FK to the FormTemplate used to render this certificate (null = old-system certificate)
	public Guid? FormTemplateId { get; set; }

	public FormTemplate? FormTemplate { get; set; }

	public List<Permit> Permits { get; set; } = new();

}
