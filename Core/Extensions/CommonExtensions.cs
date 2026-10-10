using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

using PermitPro.Core.Data;
using PermitPro.Core.Interceptors;
using PermitPro.Core.Interfaces;
using PermitPro.Core.Services;

namespace PermitPro.Core.Extensions;

public static class CommonExtensions
{
	public static void AddPermitProServices(this IServiceCollection services)
	{
		services.AddMemoryCache();

		// Keys persisted to the shared database (not the per-machine default) so encrypted
		// AppSettings decrypt consistently across every dev machine and deployed instance.
		services.AddDataProtection()
			.PersistKeysToDbContext<ApplicationDbContext>()
			.SetApplicationName("PermitPro");
		services.AddSingleton<UpdateAuditableEntitiesInterceptor>();
		services.AddScoped<ICurrentUserService, CurrentUserService>();
		services.AddScoped<ISystemConfigurationService, SystemConfigurationService>();
		services.AddScoped<IPermitService, PermitService>();
		services.AddScoped<IPermitPdfService, Services.LegacyPermitPdf.LegacyPermitPdfService>();
		services.AddScoped<IUserService, UserService>();
		services.AddScoped<IMessageService, MessageService>();
		services.AddScoped<ILogService, LogService>();
		services.AddScoped<IAppSettingsService, AppSettingsService>();
		services.AddScoped<IFormControlService, FormControlService>();
		services.AddScoped<IFormTemplateService, FormTemplateService>();
		services.AddScoped<ICertificateService, CertificateService>();
		services.AddScoped<IQuestPdfService, QuestPdfService>();
	}
}
