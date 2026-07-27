using Microsoft.EntityFrameworkCore;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

namespace PermitPro.Core.Services;

public class FormTemplateService : IFormTemplateService
{
    private readonly ApplicationDbContext _db;

    public FormTemplateService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<FormTemplate>> GetAllAsync(Guid companyId, FormTemplateTypeEnum? type = null, CancellationToken ct = default)
    {
        var query = _db.FormTemplates
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (type.HasValue)
            query = query.Where(x => x.TemplateType == type.Value);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task<FormTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.FormTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<FormTemplate?> GetActiveAsync(Guid companyId, FormTemplateTypeEnum type, CancellationToken ct = default)
        => await _db.FormTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.TemplateType == type && x.IsActive, ct);

    public async Task<FormTemplate> CreateAsync(FormTemplate template, CancellationToken ct = default)
    {
        template.Id = Guid.NewGuid();
        template.CreatedWhen = DateTime.UtcNow;

        if (template.IsActive)
            await ClearActiveAsync(template.CompanyId, template.TemplateType, ct);

        _db.FormTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<FormTemplate> UpdateAsync(FormTemplate template, CancellationToken ct = default)
    {
        template.UpdatedWhen = DateTime.UtcNow;

        if (template.IsActive)
            await ClearActiveAsync(template.CompanyId, template.TemplateType, ct, excludeId: template.Id);

        _db.FormTemplates.Update(template);
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task SetActiveAsync(Guid id, Guid companyId, FormTemplateTypeEnum type, CancellationToken ct = default)
    {
        await ClearActiveAsync(companyId, type, ct, excludeId: id);

        var template = await _db.FormTemplates.FindAsync([id], ct);
        if (template is null) return;

        template.IsActive = true;
        template.UpdatedWhen = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default)
    {
        var template = await _db.FormTemplates.FindAsync([id], ct);
        if (template is null) return;

        template.IsDeleted = true;
        template.DeletedWhen = DateTime.UtcNow;
        template.DeletedBy = deletedBy;
        await _db.SaveChangesAsync(ct);
    }

    private async Task ClearActiveAsync(Guid companyId, FormTemplateTypeEnum type, CancellationToken ct, Guid? excludeId = null)
    {
        var active = await _db.FormTemplates
            .Where(x => x.CompanyId == companyId && x.TemplateType == type && x.IsActive)
            .ToListAsync(ct);

        foreach (var t in active.Where(t => t.Id != excludeId))
        {
            t.IsActive = false;
            t.UpdatedWhen = DateTime.UtcNow;
        }
    }
}
