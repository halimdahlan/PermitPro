using PermitPro.Core.Entities;

namespace PermitPro.Core.Interfaces;

public interface IFormControlService
{
    Task<IEnumerable<FormControl>> GetAllAsync(Guid companyId, CancellationToken ct = default);

    Task<FormControl?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<FormControl> CreateAsync(FormControl control, CancellationToken ct = default);

    Task<FormControl> UpdateAsync(FormControl control, CancellationToken ct = default);

    Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default);
}
