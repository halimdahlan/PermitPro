using Microsoft.EntityFrameworkCore;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

namespace PermitPro.Core.Services;

public class CertificateService : ICertificateService
{
    private readonly ApplicationDbContext _db;

    public CertificateService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Certificate>> GetByPermitAsync(Guid permitId, CancellationToken ct = default)
        => await _db.Certificates
            .AsNoTracking()
            .Include(c => c.FormTemplate)
            .Where(c => c.Permits.Any(p => p.Id == permitId))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<Certificate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Certificates
            .AsNoTracking()
            .Include(c => c.FormTemplate)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Certificate> CreateAsync(Guid permitId, Guid? templateId, string name, string? description, CancellationToken ct = default)
    {
        var permit = await _db.Permits.FindAsync([permitId], ct)
            ?? throw new InvalidOperationException("Permit not found.");

        FormTemplate? template = templateId.HasValue
            ? await _db.FormTemplates.FindAsync([templateId.Value], ct)
            : null;

        var cert = new Certificate
        {
            Name = name,
            Description = description,
            FormTemplateId = template?.Id,
            FormTemplate = template,
        };

        _db.Certificates.Add(cert);
        permit.Certificates.Add(cert);
        await _db.SaveChangesAsync(ct);
        return cert;
    }

    public async Task<Certificate> SaveDataAsync(Guid id, string certificateDataJson, CancellationToken ct = default)
    {
        var cert = await _db.Certificates.FindAsync([id], ct)
            ?? throw new InvalidOperationException("Certificate not found.");

        cert.CertificateDataJson = certificateDataJson;
        cert.UpdatedWhen = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return cert;
    }

    public async Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default)
    {
        var cert = await _db.Certificates.FindAsync([id], ct);
        if (cert is null) return;

        cert.IsDeleted = true;
        cert.DeletedWhen = DateTime.UtcNow;
        cert.DeletedBy = deletedBy;
        await _db.SaveChangesAsync(ct);
    }
}
