#nullable disable

using Microsoft.EntityFrameworkCore;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Interfaces;

namespace PermitPro.Core.Services;

public class LogService : ILogService
{
	private readonly ApplicationDbContext _dbContext;
	//private readonly IHttpContextAccessor _contextAccessor;

	public LogService(ApplicationDbContext context)
	{
		_dbContext = context;
	}

	public async Task LogMessageAsync(LogTypeEnum logType, string category, string message, UserInfo user)
	{
		await LogMessageInternalAsync(logType, category, message, user);
	}

	public async Task LogMessageAsync(LogTypeEnum logType, string category, string message, UserInfo user, string url)
	{
		await LogMessageInternalAsync(logType, category, message, user, url);
	}

	public async Task LogMessageAsync(LogTypeEnum logType, string category, string message, UserInfo user, string url, string serializedData)
	{
		await LogMessageInternalAsync(logType, category, message, user, url, serializedData);
	}

	public async Task LogMessageAsync(LogTypeEnum logType, string category, string message, UserInfo user, string url, string serializedData, string entity)
	{
		await LogMessageInternalAsync(logType, category, message, user, url, serializedData, entity);
	}

	public void LogMessage(LogTypeEnum logType, string category, string message, UserInfo user)
	{
		LogMessageInternal(logType, category, message, user);
	}

	public void LogMessage(LogTypeEnum logType, string category, string message, UserInfo user, string url)
	{
		LogMessageInternal(logType, category, message, user, url);
	}

	public void LogMessage(LogTypeEnum logType, string category, string message, UserInfo user, string url, string serializedData)
	{
		LogMessageInternal(logType, category, message, user, url, serializedData);
	}

	public void LogMessage(LogTypeEnum logType, string category, string message, UserInfo user, string url, string serializedData, string entity)
	{
		LogMessageInternal(logType, category, message, user, url, serializedData, entity);
	}

	private async Task LogMessageInternalAsync(LogTypeEnum logType, string category, string message, UserInfo user = null, string url = null, string serializedData = null, string entity = null)
	{
		var companyId = await ResolveCompanyIdAsync(user);
		var log = new AuditLog
		{
			Id = Guid.NewGuid(),
			LogType = logType,
			Category = category,
			EntityName = entity,
			Description = message,
			Url = url,
			SerializedData = serializedData,
			AuditLogUser = user,
			CompanyId = companyId,
			CreatedBy = user != null ? Guid.Parse(user.Id) : null,
			CreatedWhen = DateTime.UtcNow.ToUniversalTime(),
		};

		_dbContext.AuditLogs.Add(log);
		await _dbContext.SaveChangesAsync();
	}


	private void LogMessageInternal(LogTypeEnum logType, string category, string message, UserInfo user = null, string url = null, string serializedData = null, string entity = null)
	{
		var companyId = ResolveCompanyId(user);
		var log = new AuditLog
		{
			Id = Guid.NewGuid(),
			LogType = logType,
			Category = category,
			EntityName = entity,
			Description = message,
			Url = url,
			SerializedData = serializedData,
			AuditLogUser = user,
			CompanyId = companyId,
			CreatedBy = user != null ? Guid.Parse(user.Id) : null,
			CreatedWhen = DateTime.UtcNow.ToUniversalTime(),
		};

		_dbContext.AuditLogs.Add(log);
		_dbContext.SaveChanges();
	}

	private async Task<Guid?> ResolveCompanyIdAsync(UserInfo user)
	{
		if (user == null) return null;
		if (user.UserCompany != null) return user.UserCompany.Id;
		return await _dbContext.Users
			.Where(u => u.Id == user.Id)
			.Select(u => u.UserCompany != null ? (Guid?)u.UserCompany.Id : null)
			.FirstOrDefaultAsync();
	}

	private Guid? ResolveCompanyId(UserInfo user)
	{
		if (user == null) return null;
		if (user.UserCompany != null) return user.UserCompany.Id;
		return _dbContext.Users
			.Where(u => u.Id == user.Id)
			.Select(u => u.UserCompany != null ? (Guid?)u.UserCompany.Id : null)
			.FirstOrDefault();
	}
}
