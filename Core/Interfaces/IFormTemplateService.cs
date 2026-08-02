using PermitPro.Core.Entities;
using PermitPro.Core.Enums;

namespace PermitPro.Core.Interfaces;

public interface IFormTemplateService
{
    Task<IEnumerable<FormTemplate>> GetAllAsync(Guid companyId, FormTemplateTypeEnum? type = null, CancellationToken ct = default);

    Task<FormTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<FormTemplate> CreateAsync(FormTemplate template, CancellationToken ct = default);

    Task<FormTemplate> UpdateAsync(FormTemplate template, CancellationToken ct = default);

    Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default);
}
