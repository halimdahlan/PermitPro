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
            .Where(x => x.CompanyId == companyId && !x.IsDeleted);

        if (type.HasValue)
            query = query.Where(x => x.TemplateType == type.Value);

        return await query
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task<FormTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.FormTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<FormTemplate> CreateAsync(FormTemplate template, CancellationToken ct = default)
    {
        template.Id = Guid.NewGuid();
        template.CreatedWhen = DateTime.UtcNow;

        _db.FormTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<FormTemplate> UpdateAsync(FormTemplate template, CancellationToken ct = default)
    {
        var tracked = await _db.FormTemplates.FindAsync([template.Id], ct);
        if (tracked is null) return template;

        tracked.Name             = template.Name;
        tracked.Description      = template.Description;
        tracked.TemplateType     = template.TemplateType;
        tracked.LayoutJson       = template.LayoutJson;
        tracked.HeaderConfigJson = template.HeaderConfigJson;
        tracked.FooterConfigJson = template.FooterConfigJson;
        tracked.UpdatedWhen      = DateTime.UtcNow;
        tracked.UpdatedBy        = template.UpdatedBy;

        await _db.SaveChangesAsync(ct);
        return tracked;
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
}
