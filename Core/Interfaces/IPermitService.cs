using Microsoft.AspNetCore.Http;

using PermitPro.Core.Entities;

namespace PermitPro.Core.Interfaces;

public interface IPermitService : IEntityOperation<Permit>
{
	Task ClosePermitAsync();
	Task ClosePermitAsync(Guid permitId, string notes);

	Task UpdateCertificateAsync();
	Task UpdateCertificateAsync(Guid permitId, Guid companyId, string permitForm);

	Task SetPermitStatusAsync();
	Task SetPermitStatusAsync(Guid permitId, string mode, string comments);

	Task CreateAsync(Guid companyId, string permitForm, string submissionType, IFormFileCollection files);
	Task UpdateAsync(Guid permitId, Guid companyId, string permitForm, string submissionType, IFormFileCollection files);

	// New-system: stores field values as PermitDataJson, no Scriban template
	Task<Guid> CreateNewSystemAsync(Guid companyId, Guid templateId, string description, DateTime? startDateTime, DateTime? endDateTime, string permitDataJson, string submissionType, IFormFileCollection files, CancellationToken ct = default);
	Task UpdateNewSystemAsync(Guid permitId, Guid companyId, string description, DateTime? startDateTime, DateTime? endDateTime, string permitDataJson, string submissionType, IFormFileCollection files, CancellationToken ct = default);

	IEnumerable<Permit> GetPermitsBySite(Site site);

	IEnumerable<Permit> GetPermitsBySite(Guid id);
}
