using PermitPro.Core.Entities;

namespace PermitPro.Core.Interfaces;

public interface IFormControlService
{
    Task<IEnumerable<FormControl>> GetAllAsync(Guid companyId, CancellationToken ct = default);

    Task<FormControl?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<FormControl> CreateAsync(FormControl control, CancellationToken ct = default);

    Task<FormControl> UpdateAsync(FormControl control, CancellationToken ct = default);

    Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default);

    /// <summary>Why this control shouldn't be deleted (it is placed in one or more templates), or null when it is free to go.</summary>
    Task<string?> GetDeleteBlockReasonAsync(Guid id, CancellationToken ct = default);
}
