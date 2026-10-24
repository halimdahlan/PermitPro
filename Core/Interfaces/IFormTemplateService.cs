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

    /// <summary>Why this template shouldn't be deleted (permits or certificates were made from it), or null when it is free to go.</summary>
    Task<string?> GetDeleteBlockReasonAsync(Guid id, CancellationToken ct = default);
}
