using Microsoft.EntityFrameworkCore;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

namespace PermitPro.Core.Services;

public class FormControlService : IFormControlService
{
    private readonly ApplicationDbContext _db;

    public FormControlService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<FormControl>> GetAllAsync(Guid companyId, CancellationToken ct = default)
        => await _db.FormControls
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

    public async Task<FormControl?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.FormControls
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<FormControl> CreateAsync(FormControl control, CancellationToken ct = default)
    {
        control.Id = Guid.NewGuid();
        control.CreatedWhen = DateTime.UtcNow;
        _db.FormControls.Add(control);
        await _db.SaveChangesAsync(ct);
        return control;
    }

    public async Task<FormControl> UpdateAsync(FormControl control, CancellationToken ct = default)
    {
        control.UpdatedWhen = DateTime.UtcNow;
        _db.FormControls.Update(control);
        await _db.SaveChangesAsync(ct);
        return control;
    }

    public async Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default)
    {
        var control = await _db.FormControls.FindAsync([id], ct);
        if (control is null) return;

        control.IsDeleted = true;
        control.DeletedWhen = DateTime.UtcNow;
        control.DeletedBy = deletedBy;
        await _db.SaveChangesAsync(ct);
    }
}
