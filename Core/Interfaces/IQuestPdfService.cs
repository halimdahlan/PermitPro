using PermitPro.Core.Entities;

namespace PermitPro.Core.Interfaces;

public interface IQuestPdfService
{
    Task<byte[]> GenerateAsync(Permit permit, CancellationToken ct = default);
}
