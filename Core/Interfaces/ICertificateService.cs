using PermitPro.Core.Entities;

namespace PermitPro.Core.Interfaces;

public interface ICertificateService
{
    Task<IEnumerable<Certificate>> GetByPermitAsync(Guid permitId, CancellationToken ct = default);

    Task<Certificate?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Certificate> CreateAsync(Guid permitId, Guid? templateId, string name, string? description, CancellationToken ct = default);

    Task<Certificate> SaveDataAsync(Guid id, string certificateDataJson, CancellationToken ct = default);

    Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default);
}
