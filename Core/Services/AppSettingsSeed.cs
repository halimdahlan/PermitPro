using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;

namespace PermitPro.Core.Services;

public static class AppSettingsSeed
{
    public static async Task SeedDefaultsAsync(IServiceProvider sp, IConfiguration config)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("AppSettings");

        if (await db.AppSettingCategories.AnyAsync())
            return;

        var emailCategoryId = Guid.NewGuid();
        var generalCategoryId = Guid.NewGuid();
        var workflowCategoryId = Guid.NewGuid();
        var companyId = Guid.Parse("92cc7520-4f7d-41e8-a4ae-0d74743a9bdb");

        var categories = new List<AppSettingCategory>
        {
            new()
            {
                Id = generalCategoryId,
                CompanyId = companyId,
                Name = "general",
                DisplayName = "General",
                Description = "General application settings",
                Icon = "fa-sliders",
                SortOrder = 1
            },
            new()
            {
                Id = emailCategoryId,
                CompanyId = companyId,
                Name = "email",
                DisplayName = "Email",
                Description = "SMTP email server configuration",
                Icon = "fa-envelope",
                SortOrder = 2
            },
            new()
            {
                Id = workflowCategoryId,
                CompanyId = companyId,
                Name = "workflow",
                DisplayName = "Workflow",
                Description = "Workflow settings used in permits",
                Icon = "fa-diagram-nested",
                SortOrder = 3
            }
        };

        await db.AppSettingCategories.AddRangeAsync(categories);

        var emailSettings = new List<AppSetting>
        {
            new() { Id = Guid.NewGuid(), CategoryId = emailCategoryId, CompanyId = companyId, Key = "smtp_server",     DisplayName = "SMTP Server",   DataType = "text",     IsEncrypted = false, SortOrder = 1, Value = "mail5008.site4now.net" },
            new() { Id = Guid.NewGuid(), CategoryId = emailCategoryId, CompanyId = companyId, Key = "smtp_port",       DisplayName = "SMTP Port",     DataType = "number",   IsEncrypted = false, SortOrder = 2, Value = "8889" },
            new() { Id = Guid.NewGuid(), CategoryId = emailCategoryId, CompanyId = companyId, Key = "sender_name",     DisplayName = "Sender Name",   DataType = "text",     IsEncrypted = false, SortOrder = 3, Value = "no-reply@permitpro.app" },
            new() { Id = Guid.NewGuid(), CategoryId = emailCategoryId, CompanyId = companyId, Key = "sender_email",    DisplayName = "Sender Email",  DataType = "email",    IsEncrypted = false, SortOrder = 4, Value = "no-reply@permitpro.app" },
            new() { Id = Guid.NewGuid(), CategoryId = emailCategoryId, CompanyId = companyId, Key = "email_username",  DisplayName = "Username",      DataType = "text",     IsEncrypted = false, SortOrder = 5, Value = "no-reply@permitpro.app" },
            new() { Id = Guid.NewGuid(), CategoryId = emailCategoryId, CompanyId = companyId, Key = "email_password",  DisplayName = "Password",      DataType = "password", IsEncrypted = true,  SortOrder = 6, Value = Encrypt(protector, "SMwYLyR7IHq*I$lE") },
        };

        await db.AppSettings.AddRangeAsync(emailSettings);

        var generalSettings = new List<AppSetting>
        {
            new() { Id = Guid.NewGuid(), CategoryId = generalCategoryId, CompanyId = companyId, Key = "application_domain",        DisplayName = "Application Domain",               DataType = "text",   IsEncrypted = false, SortOrder = 1, Value = "uat.permitpro.app" },
            new() { Id = Guid.NewGuid(), CategoryId = generalCategoryId, CompanyId = companyId, Key = "user_create_limit",         DisplayName = "User Create Limit",                DataType = "number", IsEncrypted = false, SortOrder = 2, Value = "15" },
            new() { Id = Guid.NewGuid(), CategoryId = generalCategoryId, CompanyId = companyId, Key = "upload_max_file_size",      DisplayName = "Maximum Upload File Size (bytes)", DataType = "number", IsEncrypted = false, SortOrder = 3, Value = "3145728" },
            new() { Id = Guid.NewGuid(), CategoryId = generalCategoryId, CompanyId = companyId, Key = "upload_max_file_count",     DisplayName = "Maximum Upload File Count",        DataType = "number", IsEncrypted = false, SortOrder = 4, Value = "5" },
            new() { Id = Guid.NewGuid(), CategoryId = generalCategoryId, CompanyId = companyId, Key = "upload_allowed_file_types",       DisplayName = "Allowed Upload File Types",                       DataType = "text",   IsEncrypted = false, SortOrder = 5, Value = "pdf,docx,jpg,jpeg,png" },
            new() { Id = Guid.NewGuid(), CategoryId = generalCategoryId, CompanyId = companyId, Key = "new_permit_form_cutover_date", DisplayName = "New Permit Form Cutover Date (yyyy-MM-dd)", DataType = "text",   IsEncrypted = false, SortOrder = 6, Value = "" },
        };

        await db.AppSettings.AddRangeAsync(generalSettings);

        var workflowSettings = new List<AppSetting>
        {
            new() { Id = Guid.NewGuid(), CategoryId = workflowCategoryId, CompanyId = companyId, Key = "suspended_autoresume_days", DisplayName = "Auto-resume Suspended Permit (day)", DataType = "text", IsEncrypted = false, SortOrder = 1, Value = "10" },
        };

        await db.AppSettings.AddRangeAsync(workflowSettings);

        await db.SaveChangesAsync();
    }

    private static string? Encrypt(IDataProtector protector, string? value) => string.IsNullOrEmpty(value) ? value : protector.Protect(value);
}
