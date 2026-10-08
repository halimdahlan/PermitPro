using MailKit.Net.Smtp;

using Microsoft.Extensions.Logging;

using MimeKit;

using PermitPro.Core.Data;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;
using PermitPro.Core.Models;

namespace PermitPro.Core.Services;

public class MessageService : IMessageService
{
	private readonly ApplicationDbContext _dbContext;
	private readonly IAppSettingsService _appSettings;
	private readonly ILogService _logService;
	private readonly ICurrentUserService _currentUserService;
	private readonly ILogger<MessageService> _logger;

	public MessageService(ApplicationDbContext dbContext, IAppSettingsService appSettings, ILogService logService, ICurrentUserService currentUserService, ILogger<MessageService> logger)
	{
		_dbContext = dbContext;
		_appSettings = appSettings;
		_logService = logService;
		_currentUserService = currentUserService;
		_logger = logger;
	}

	public async Task SendEmailAsync(EmailInfo emailInfo)
	{
		var currentUser = _currentUserService.GetCurrentUser();
		var companyId = _currentUserService.GetCurrentCompanyId();

		var server = await _appSettings.GetValueAsync(companyId, "email", "smtp_server");
		var port = await _appSettings.GetIntAsync(companyId, "email", "smtp_port");
		var senderName = await _appSettings.GetValueAsync(companyId, "email", "sender_name");
		var senderEmail = await _appSettings.GetValueAsync(companyId, "email", "sender_email");
		var userName = await _appSettings.GetValueAsync(companyId, "email", "email_username");
		var password = await _appSettings.GetValueAsync(companyId, "email", "email_password");

		var recipientName = emailInfo.Name;
		var recipientEmail = emailInfo.Email;

      var bodyBuilder = new BodyBuilder
      {
         HtmlBody = emailInfo.Body
      };

      var mailMessage = new MimeMessage();
		mailMessage.From.Add(new MailboxAddress(senderName, senderEmail!));

		if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
		{
			recipientEmail = "workflow@permitpro.app";
		}

		mailMessage.To.Add(new MailboxAddress(recipientName, recipientEmail!));
		mailMessage.Bcc.Add(new MailboxAddress("Workflow Admin", "workflow@permitpro.app"));

		mailMessage.Subject = emailInfo.Subject ?? string.Empty;
		mailMessage.Body = bodyBuilder.ToMessageBody();

		using var client = new SmtpClient();

		try
		{
			await SmtpHelper.ConnectAsync(client, server!, port);
			await client.AuthenticateAsync(userName!, password!);

			await client.SendAsync(mailMessage);
			await client.DisconnectAsync(true);

			_logger.LogInformation("Email sent to {RecipientName} ({RecipientEmail}) via {SmtpServer}", recipientName, recipientEmail, server);
			var logMessage = $"Email has been sent to: {recipientName} ({recipientEmail}).";
			await _logService.LogMessageAsync(Enums.LogTypeEnum.Information, "SENDEMAIL", logMessage, currentUser!);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to send email to {RecipientName} ({RecipientEmail}) via {SmtpServer}", recipientName, recipientEmail, server);
			var logMessage = $"Error sending email to: {recipientName} ({recipientEmail}). Error: {ex.Message}";
			await _logService.LogMessageAsync(Enums.LogTypeEnum.Error, "SENDEMAIL", logMessage, currentUser!);
		}
	}


	public async Task SendEmailAsync(EmailInfo emailInfo, Guid companyId)
	{
		var currentUser = _currentUserService.GetCurrentUser();

		var server = await _appSettings.GetValueAsync(companyId, "email", "smtp_server");
		var port = await _appSettings.GetIntAsync(companyId, "email", "smtp_port");
		var senderName = await _appSettings.GetValueAsync(companyId, "email", "sender_name");
		var senderEmail = await _appSettings.GetValueAsync(companyId, "email", "sender_email");
		var userName = await _appSettings.GetValueAsync(companyId, "email", "email_username");
		var password = await _appSettings.GetValueAsync(companyId, "email", "email_password");

		var recipientName = emailInfo.Name;
		var recipientEmail = emailInfo.Email;

      var bodyBuilder = new BodyBuilder
      {
         HtmlBody = emailInfo.Body
      };

      var mailMessage = new MimeMessage();
		mailMessage.From.Add(new MailboxAddress(senderName, senderEmail!));

		if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
		{
			recipientEmail = "workflow@permitpro.app";
		}

		mailMessage.To.Add(new MailboxAddress(recipientName, recipientEmail!));
		mailMessage.Bcc.Add(new MailboxAddress("Workflow Admin", "workflow@permitpro.app"));

		mailMessage.Subject = emailInfo.Subject ?? string.Empty;
		mailMessage.Body = bodyBuilder.ToMessageBody();

		using var client = new SmtpClient();

		try
		{
			await SmtpHelper.ConnectAsync(client, server!, port);
			await client.AuthenticateAsync(userName!, password!);

			await client.SendAsync(mailMessage);
			await client.DisconnectAsync(true);

			_logger.LogInformation("Email sent to {RecipientName} ({RecipientEmail}) for company {CompanyId}", recipientName, recipientEmail, companyId);
			var logMessage = $"Email has been sent to: {recipientName} ({recipientEmail}).";
			await _logService.LogMessageAsync(Enums.LogTypeEnum.Information, "SENDEMAIL", logMessage, currentUser!);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to send email to {RecipientName} ({RecipientEmail}) for company {CompanyId}", recipientName, recipientEmail, companyId);
			var logMessage = $"Error sending email to: {recipientName} ({recipientEmail}). Error: {ex.Message}";
			await _logService.LogMessageAsync(Enums.LogTypeEnum.Error, "SENDEMAIL", logMessage, currentUser!);
		}
	}


	public Task SendSMSAsync(string phoneNumber, string message)
	{
		throw new NotImplementedException();
	}


	public Task SaveToDatabaseAsync(string message)
	{
		throw new NotImplementedException();
	}
}
