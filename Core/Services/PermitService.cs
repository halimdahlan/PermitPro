#nullable disable

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System.Text.Json;

using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;
using PermitPro.Core.Models;

using Scriban;

namespace PermitPro.Core.Services;

public class PermitService : IPermitService
{
	private readonly ApplicationDbContext _dbContext;
	private readonly PTWSettings _ptwSettings;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly IMessageService _messageService;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly ICurrentUserService _currentUserService;
	private readonly ILogService _logService;
	private readonly INotificationPushService _pushService;
	private readonly IAppSettingsService _appSettings;
	private readonly ILogger<PermitService> _logger;

	public PermitService(
		ApplicationDbContext context
		, PTWSettings ptwSettings
		, IWebHostEnvironment webHostEnvironment
		, IMessageService messageService
		, IHttpContextAccessor httpContextAccessor
		, ICurrentUserService currentUserService
		, ILogService logService
		, INotificationPushService pushService
		, IAppSettingsService appSettingsService
		, ILogger<PermitService> logger)
	{
		_dbContext = context;
		_ptwSettings = ptwSettings;
		_webHostEnvironment = webHostEnvironment;
		_messageService = messageService;
		_httpContextAccessor = httpContextAccessor;
		_currentUserService = currentUserService;
		_logService = logService;
		_pushService = pushService;
		_appSettings = appSettingsService;
		_logger = logger;
	}


	public async Task CreateAsync()
	{
		var form = _httpContextAccessor.HttpContext.Request.Form;
		await CreateAsync(
			Guid.Parse(form["CompanyId"]),
			form["PermitForm"],
			form["SubmissionType"],
			form.Files);
	}

	public async Task CreateAsync(Guid companyId, string permitForm, string submissionType, IFormFileCollection files)
	{
		var company = companyId;
		var permitId = Guid.NewGuid();
		var maxNum = 0;

		var formData = permitForm;
		var isDraft = submissionType == "draft";

		//Get the last running number for permit, increase running number to 1
		var lastRunningNo = _dbContext.Permits
			.Include(f => f.Company)
			.OrderByDescending(f => f.CreatedWhen)
			.FirstOrDefault(f => f.Company.Id == company);

		if (lastRunningNo != null)
		{
			maxNum = lastRunningNo.RunningNumber;
		}

		maxNum++;

		// Create a new permit with posted values
		using var jsonPermit = JsonDocument.Parse(formData);
		var generalCreate = jsonPermit.RootElement.GetProperty("general");

		{
			var certCountCreate = generalCreate.GetProperty("certificates").GetArrayLength();

			// Get current available workflow
			var workflow = _dbContext.Workflows
				.Include(e => e.WorkflowCompany)
				.FirstOrDefault(wf => wf.IsActive && wf.WorkflowCompany.Id == company && wf.HasCertificate == (certCountCreate > 0));

			if (workflow != null)
			{
				var locationId = generalCreate.GetProperty("location").GetProperty("id").GetString();
				var loc = _dbContext.Sites.FirstOrDefault(f => f.Id.ToString().ToLower() == locationId);

				var userCompany = _dbContext.Companies.SingleOrDefault(f => f.Id == company);

				var permit = new Permit
				{
					Id = permitId,
					PermitNo = string.Format("{0:000000}", maxNum),
					PermitForm = permitForm,
					RunningNumber = maxNum,
					PermitStatus = isDraft ? PermitStatusEnum.Draft : PermitStatusEnum.Pending,
					Site = loc,
					Company = userCompany,
					CreatedWhen = DateTime.UtcNow.ToUniversalTime(),
					CreatedBy = Guid.Parse(_currentUserService.GetCurrentUser().Id),
				};

				_dbContext.Permits.Add(permit);
				_dbContext.SaveChanges();

				// Add audit log to database
				var currentDate = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen);
				var currentUser = _currentUserService.GetCurrentUser();
				var fullName = $"{currentUser.FirstName} {currentUser.LastName}";
				var logMessage = $"A new permit [PTW{permit.PermitNo}] has been created by {fullName.Trim()} ({currentUser.Email}) ";
				logMessage += $"on {currentDate:dd/MM/yyyy hh:mm tt}.";

				await _logService.LogMessageAsync(LogTypeEnum.Information, "CREATE_PERMIT", logMessage, currentUser);

				// Upload any attached documents
				if (files != null && files.Count > 0)
				{
					var folderName = Path.Combine(_webHostEnvironment.WebRootPath, "attachments", "permits", permitId.ToString().ToLower());

					if (!Directory.Exists(folderName))
					{
						Directory.CreateDirectory(folderName);
					}

					foreach (var item in files)
					{
						var file = new FileInfo(item.FileName);
						var filePath = Path.Combine(folderName, file.Name);

						using (var fs = new FileStream(filePath, FileMode.Create))
						{
							await item.CopyToAsync(fs);
							fs.Flush();
						}
					}

					string[] filePaths = Directory.GetFiles(folderName);

					foreach (var filePath in filePaths)
					{
						var fi = new FileInfo(filePath);

						var attachment = new Attachment
						{
							FileName = fi.Name,
							ContentType = fi.Extension.Remove(0),
							Permit = permit,
							FileSize = (int)fi.Length,
						};

						_dbContext.Attachments.Add(attachment);
					}
				}

				WorkflowStep workflowStep;

				if (!isDraft)
				{
					workflowStep = _dbContext.WorkflowSteps
						.Include(wfs => wfs.Approvers)
						.Include(wf => wf.WorkflowStepWorkflow)
						.Where(wfs => wfs.WorkflowStepWorkflow == workflow && wfs.Name != "Draft" && wfs.Name != "Completed")
						.OrderBy(wfs => wfs.StepOrder)
						.FirstOrDefault();
				}
				else
				{
					workflowStep = _dbContext.WorkflowSteps
						.Include(wfs => wfs.Approvers)
						.Include(wf => wf.WorkflowStepWorkflow)
						.Where(wfs => wfs.WorkflowStepWorkflow == workflow && wfs.Name == "Draft")
						.OrderBy(wfs => wfs.StepOrder)
						.FirstOrDefault();
				}


				if (!isDraft && workflowStep.Approvers.Count > 0)
				{
					// Get email template
					var htmlTemplate = File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "templates", "html", "wf-pending-approval.html"));
					var template = Template.Parse(htmlTemplate);

					// Generate email + internal notification for each approver
					var permitNoCreate = string.Format("PTW{0:000000}", permit.RunningNumber);
					foreach (var approver in workflowStep.Approvers)
					{
						var approverName = $"{approver.FirstName} {approver.LastName}";
						var approvalLink = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host.Value}/account/login?company={company}&entity=permits&id={permitId}&origin=email";
						var notifLink = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host.Value}/{company}/permits/edit/{permitId}";

						var templateData = new
						{
							RecipientName = approverName,
							WorkflowName = workflow.Name,
							PermitNo = permitNoCreate,
							DateSubmitted = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen).ToString("dd/MM/yyyy hh:mm tt"),
							ApprovalLink = approvalLink,
						};

						var renderedHtml = template.Render(templateData);
						await _messageService.SendEmailAsync(new EmailInfo
						{
							Name = approverName,
							Email = approver.Email,
							Subject = $"Pending approval task for {permitNoCreate}",
							Body = renderedHtml
						});

						_dbContext.Notifications.Add(new Notification
						{
							Title = "Approval Required",
							Message = $"You have a pending approval task for {permitNoCreate}.",
							Url = notifLink,
							IsRead = false,
							IsArchived = false,
							NotificationUser = approver,
						});

						await _pushService.PushAsync(approver.Id, "Approval Required", $"You have a pending approval task for {permitNoCreate}.");
					}
				}


				// Update workflow step in permit
				if (workflowStep.WorkflowStepWorkflow != null) permit.PermitWorkflow = workflowStep.WorkflowStepWorkflow;
				permit.PermitWorkflowStep = workflowStep;
				_dbContext.Permits.Update(permit);

				// Create a workflow history entry
				var wfh = new WorkflowHistory
				{
					Permit = permit,
					HistoryWorkflow = workflow,
					HistoryWorkflowStep = workflowStep,
					Status = isDraft ? WorkflowStatusEnum.Draft : WorkflowStatusEnum.Pending,
					Comments = isDraft ? "Permit's draft information has been successfully saved." : "Permit is now pending for approval.",
				};

				_dbContext.WorkflowHistories.Add(wfh);

				await _dbContext.SaveChangesAsync();
			}
			else
			{
				throw new Exception("Invalid workflow");
			}
		}
	}


	public async Task CreateAsync(Permit entity)
	{
		_dbContext.Permits.Add(entity);
		await _dbContext.SaveChangesAsync();
	}


	public async Task UpdateAsync(Permit entity)
	{
		_dbContext.Permits.Update(entity);
		await _dbContext.SaveChangesAsync();
	}


	public async Task UpdateAsync()
	{
		var form = _httpContextAccessor.HttpContext.Request.Form;
		await UpdateAsync(
			Guid.Parse(form["PermitId"]),
			Guid.Parse(form["CompanyId"]),
			form["PermitForm"],
			form["SubmissionType"],
			form.Files);
	}

	public async Task UpdateAsync(Guid permitId, Guid companyId, string permitForm, string submissionType, IFormFileCollection files)
	{
		var formData = permitForm;
		var isDraft = submissionType == "draft";

		// Create a new permit with posted values
		using var jsonPermit = JsonDocument.Parse(formData);
		var generalUpdate = jsonPermit.RootElement.GetProperty("general");

		{
			var certCountUpdate = generalUpdate.GetProperty("certificates").GetArrayLength();

			// Get current available workflow
			var workflow = _dbContext.Workflows
				.Include(e => e.WorkflowCompany)
				.FirstOrDefault(wf => wf.IsActive && wf.WorkflowCompany.Id == companyId && wf.HasCertificate == (certCountUpdate > 0));

			if (workflow != null)
			{
				var locationId = generalUpdate.GetProperty("location").GetProperty("id").GetString();
				var loc = _dbContext.Sites.FirstOrDefault(f => f.Id.ToString().ToLower() == locationId);

				var permit = _dbContext.Permits.SingleOrDefault(f => f.Id == permitId);

				permit.PermitForm = permitForm;
				permit.PermitStatus = isDraft ? PermitStatusEnum.Draft : PermitStatusEnum.Pending;
				permit.Site = loc;
				permit.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();
				permit.UpdatedBy = Guid.Parse(_currentUserService.GetCurrentUser().Id);

				_dbContext.Permits.Update(permit);
				await _dbContext.SaveChangesAsync();

				// Add audit log to database
				var currentDate = GeneralHelper.GetDateInTimeZone(permit.UpdatedWhen.Value);
				var currentUser = _currentUserService.GetCurrentUser();
				var fullName = $"{currentUser.FirstName} {currentUser.LastName}";
				var logMessage = $"Permit [PTW{permit.PermitNo}] has been updated by {fullName.Trim()} ({currentUser.Email}) ";
				logMessage += $"on {currentDate:dd/MM/yyyy hh:mm tt}.";

				await _logService.LogMessageAsync(LogTypeEnum.Information, "UPDATE_PERMIT", logMessage, currentUser);


				// Upload any attached documents
				if (files != null && files.Count > 0)
				{
					var folderName = Path.Combine(_webHostEnvironment.WebRootPath, "attachments", "permits", permitId.ToString().ToLower());

					if (!Directory.Exists(folderName))
					{
						Directory.CreateDirectory(folderName);
					}

					foreach (var item in files)
					{
						var file = new FileInfo(item.FileName);
						var filePath = Path.Combine(folderName, file.Name);

						using (var fs = new FileStream(filePath, FileMode.Create))
						{
							await item.CopyToAsync(fs);
							fs.Flush();
						}
					}

					string[] filePaths = Directory.GetFiles(folderName);

					foreach (var filePath in filePaths)
					{
						var fi = new FileInfo(filePath);
						var currentAttachment = _dbContext.Attachments.FirstOrDefault(f => f.FileName == fi.Name);

						if (currentAttachment == null)
						{
							var attachment = new Attachment
							{
								FileName = fi.Name,
								ContentType = fi.Extension.Remove(0),
								Permit = permit,
								FileSize = (int)fi.Length,
							};

							_dbContext.Attachments.Add(attachment);
						}

					}
				}

				WorkflowStep workflowStep;

				if (isDraft)
				{
					workflowStep = _dbContext.WorkflowSteps
						.Include(wfs => wfs.Approvers)
						.Where(wfs => wfs.WorkflowStepWorkflow == workflow && wfs.Name == "Draft")
						.OrderBy(wfs => wfs.StepOrder)
						.FirstOrDefault();
				}
				else
				{
					workflowStep = _dbContext.WorkflowSteps
						.Include(wfs => wfs.Approvers)
						.Where(wfs => wfs.WorkflowStepWorkflow == workflow && wfs.Name != "Draft" && wfs.Name != "Completed")
						.OrderBy(wfs => wfs.StepOrder)
						.FirstOrDefault();
				}

				if (!isDraft && workflowStep.Approvers.Count > 0)
				{
					// Get email template
					var htmlTemplate = File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "templates", "html", "wf-pending-approval.html"));
					var template = Template.Parse(htmlTemplate);

					// Generate email + internal notification for each approver
					var permitNoUpdate = string.Format("PTW{0:000000}", permit.RunningNumber);
					foreach (var approver in workflowStep.Approvers)
					{
						var approverName = $"{approver.FirstName} {approver.LastName}";
						var approvalLink = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host.Value}/account/login?company={companyId}&entity=permits&id={permitId}&origin=email";
						var notifLink = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host.Value}/{companyId}/permits/edit/{permitId}";

						var templateData = new
						{
							RecipientName = approverName,
							WorkflowName = workflow.Name,
							PermitNo = permitNoUpdate,
							DateSubmitted = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen).ToString("dd/MM/yyyy hh:mm tt"),
							ApprovalLink = approvalLink,
						};

						var renderedHtml = template.Render(templateData);

						try
						{
							await _messageService.SendEmailAsync(new EmailInfo
							{
								Name = approverName,
								Email = approver.Email,
								Subject = $"Pending approval task for {permitNoUpdate}",
								Body = renderedHtml
							}, companyId);
						}
						catch {}

						_dbContext.Notifications.Add(new Notification
						{
							Title = "Approval Required",
							Message = $"You have a pending approval task for {permitNoUpdate}.",
							Url = notifLink,
							IsRead = false,
							IsArchived = false,
							NotificationUser = approver,
						});
						await _pushService.PushAsync(approver.Id, "Approval Required", $"You have a pending approval task for {permitNoUpdate}.");
					}
				}


				// Update workflow step in permit
				permit.PermitWorkflowStep = workflowStep;
				_dbContext.Permits.Update(permit);

				// Create a workflow history entry
				var wfh = new WorkflowHistory
				{
					Permit = permit,
					HistoryWorkflow = workflow,
					HistoryWorkflowStep = workflowStep,
					Status = isDraft ? WorkflowStatusEnum.Draft : WorkflowStatusEnum.Pending,
					Comments = isDraft ? "Permit's draft information has been successfully saved." : "Permit is now pending for approval.",
				};

				_dbContext.WorkflowHistories.Add(wfh);

				await _dbContext.SaveChangesAsync();
			}
			else
			{
				throw new Exception("Invalid workflow");
			}
		}
	}


	public async Task UpdateCertificateAsync()
	{
		var form = _httpContextAccessor.HttpContext.Request.Form;
		await UpdateCertificateAsync(
			Guid.Parse(form["PermitId"]),
			Guid.Parse(form["CompanyId"]),
			form["PermitForm"]);
	}

	public async Task UpdateCertificateAsync(Guid permitId, Guid companyId, string permitForm)
	{
		// Validate the permit JSON is parseable before saving
		JsonDocument.Parse(permitForm).Dispose();

		var permit = _dbContext.Permits.SingleOrDefault(f => f.Id == permitId);

		var updatedDate = DateTime.UtcNow.ToUniversalTime();

		permit.PermitForm = permitForm;
		permit.UpdatedWhen = updatedDate;
		permit.UpdatedBy = Guid.Parse(_currentUserService.GetCurrentUser().Id);

		_dbContext.Permits.Update(permit);
		await _dbContext.SaveChangesAsync();

		// Add audit log to database
		var currentDate = GeneralHelper.GetDateInTimeZone(updatedDate);
		var currentUser = _currentUserService.GetCurrentUser();
		var fullName = $"{currentUser.FirstName} {currentUser.LastName}";
		var logMessage = $"Permit [PTW{permit.PermitNo}] has been updated by {fullName.Trim()} ({currentUser.Email}) ";
		logMessage += $"on {currentDate:dd/MM/yyyy hh:mm tt}.";

		await _logService.LogMessageAsync(LogTypeEnum.Information, "UPDATE_PERMIT", logMessage, currentUser);
	}


	public async Task DeleteAsync(Permit entity)
	{
		var permitId = entity.Id;
		var permitNo = entity.PermitNo;

		// Delete any workflow history to this permit
		var wfh = _dbContext.WorkflowHistories
			.Include(f => f.Permit)
			.Where(f => f.Permit.Id == permitId);

		_dbContext.WorkflowHistories.RemoveRange(wfh);

		// Delete the permit
		//_dbContext.Permits.Remove(entity);
		await _dbContext.SoftDeleteAsync(entity, Guid.Parse(_currentUserService.GetCurrentUser().Id));

		// Delete file attachments
		var folderName = Path.Combine(_webHostEnvironment.WebRootPath, "attachments", "permits", permitId.ToString().ToLower());

		if (Directory.Exists(folderName))
		{
			string[] filePaths = Directory.GetFiles(folderName);

			foreach (var filePath in filePaths)
			{
				FileInfo fi = new FileInfo(filePath);

				if (fi.Exists)
				{
					fi.Delete();
				}
			}

			Directory.Delete(folderName);
		}

		await _dbContext.SaveChangesAsync();

		// Add audit log to database
		var logDate = GeneralHelper.GetDateInTimeZone(DateTime.UtcNow.ToUniversalTime());
		var currentUser = _currentUserService.GetCurrentUser();
		var fullName = $"{currentUser.FirstName} {currentUser.LastName}";
		var logMessage = $"Permit [PTW{permitNo}] has been deleted by {fullName.Trim()} ({currentUser.Email}) ";
		logMessage += $"on {logDate:dd/MM/yyyy hh:mm tt}.";

		await _logService.LogMessageAsync(LogTypeEnum.Information, "DELETE_PERMIT", logMessage, currentUser);
	}


	public async Task SetPermitStatusAsync()
	{
		var request = _httpContextAccessor.HttpContext.Request.Form;
		await SetPermitStatusAsync(
			Guid.Parse(request["PermitId"]),
			request["Mode"],
			request["Comments"]);
	}

	public async Task SetPermitStatusAsync(Guid permitId, string mode, string comments)
	{
		var currentUser = _currentUserService.GetCurrentUser();
		var fullName = $"{currentUser.FirstName} {currentUser.LastName}";
		var logMessage = string.Empty;

		var permit = _dbContext.Permits
			.Include(f => f.PermitWorkflowStep)
			.ThenInclude(wfs => wfs.WorkflowStepWorkflow)
			.SingleOrDefault(f => f.Id == permitId);

		if (permit == null)
		{
			throw new Exception($"Unable to find permit with permit ID: {permitId}");
		}

		var workflowStep = permit.PermitWorkflowStep;
		var workflow = workflowStep.WorkflowStepWorkflow;

		var workflowSteps = _dbContext.WorkflowSteps
			.Where(f => f.WorkflowStepWorkflow.Id == workflow.Id)
			.OrderBy(f => f.StepOrder);

		WorkflowStep nextWfs = null;
		WorkflowStep completedWfs = null;
		bool checkForNext = false;
		bool workflowCompleted = false;

		foreach (var step in workflowSteps)
		{
			if (checkForNext)
			{
				if (step.StepOrder == 100)
				{
					workflowCompleted = true;
					completedWfs = step;
				}
				else
				{
					nextWfs = step;
					break;
				}
			}

			checkForNext = step.Id == workflowStep.Id;
		}

		if (mode == "approve")
		{
			var dateApproved = DateTime.UtcNow.ToUniversalTime();

			// If approval step is not completed, move to the next step
			if (!workflowCompleted)
			{
				permit.PermitWorkflowStep = nextWfs;
				_dbContext.Permits.Update(permit);

				var wfh = new WorkflowHistory
				{
					Permit = permit,
					Comments = !string.IsNullOrEmpty(comments) ? comments : $"{fullName.Trim()} has approved this permit.",
					HistoryWorkflow = workflow,
					HistoryWorkflowStep = workflowStep,
					Status = WorkflowStatusEnum.Approved,
					ApprovedWhen = dateApproved,
				};
				_dbContext.WorkflowHistories.Add(wfh);

				// Notify next-step approvers
				if (nextWfs != null)
				{
					var nextApprovers = _dbContext.WorkflowSteps
						.Include(s => s.Approvers)
						.FirstOrDefault(s => s.Id == nextWfs.Id)?.Approvers ?? new();
					var permitNoNext = string.Format("PTW{0:000000}", permit.RunningNumber);
					foreach (var nextApprover in nextApprovers)
					{
						var nextLink = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host.Value}/{permit.Company?.Id}/permits//edit/{permit.Id}";

						_dbContext.Notifications.Add(new Notification
						{
							Title = "Approval Required",
							Message = $"You have a pending approval task for {permitNoNext}.",
							Url = nextLink,
							IsRead = false,
							IsArchived = false,
							NotificationUser = nextApprover,
						});

						await _pushService.PushAsync(nextApprover.Id, "Approval Required", $"You have a pending approval task for {permitNoNext}.");
					}
				}
			}
			else
			{
				permit.ApprovedDateTime = dateApproved;
				permit.ResumedDateTime = null;
				permit.RejectedDateTime = null;

				permit.PermitWorkflowStep = completedWfs;
				permit.PermitStatus = PermitStatusEnum.Approved;
				permit.UpdatedBy = Guid.Parse(currentUser.Id);
				permit.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();

				_dbContext.Permits.Update(permit);

				var wfh = new WorkflowHistory
				{
					Permit = permit,
					Comments = !string.IsNullOrEmpty(comments) ? comments : $"{fullName.Trim()} has approved this permit.",
					HistoryWorkflow = workflow,
					HistoryWorkflowStep = workflowStep,
					Status = WorkflowStatusEnum.Approved,
					ApprovedWhen = dateApproved,
				};
				_dbContext.WorkflowHistories.Add(wfh);

				// Notify permit creator: fully approved
				var creator = _dbContext.Users.FirstOrDefault(u => u.Id == permit.CreatedBy.ToString());
				if (creator != null)
				{
					var approvedPermitNo = string.Format("PTW{0:000000}", permit.RunningNumber);
					var approvedLink = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host.Value}/{permit.Company?.Id}/permits/edit/{permit.Id}";

					_dbContext.Notifications.Add(new Notification
					{
						Title = "Permit Approved",
						Message = $"{approvedPermitNo} has been fully approved.",
						Url = approvedLink,
						IsRead = false,
						IsArchived = false,
						NotificationUser = creator,
					});

					await _pushService.PushAsync(creator.Id, "Permit Approved", $"{approvedPermitNo} has been fully approved.");
				}
			}

			logMessage = $"Permit [PTW{permit.PermitNo}] has been approved by {fullName.Trim()} ({currentUser.Email}) on {GeneralHelper.GetDateInTimeZone(dateApproved):dd/MM/yyy hh:mm tt}.";
		}
		else
		{
			var dateRejected = DateTime.UtcNow.ToUniversalTime();

			permit.PermitStatus = PermitStatusEnum.Rejected;

			permit.ApprovedDateTime = null;
			permit.ResumedDateTime = null;
			permit.RejectedDateTime = dateRejected;
			permit.UpdatedBy = Guid.Parse(currentUser.Id);
			permit.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();

			_dbContext.Permits.Update(permit);

			var wfh = new WorkflowHistory
			{
				Permit = permit,
				Comments = !string.IsNullOrEmpty(comments) ? comments : $"{fullName.Trim()} has rejected this permit.",
				HistoryWorkflow = workflow,
				HistoryWorkflowStep = workflowStep,
				Status = WorkflowStatusEnum.Rejected,
				RejectedWhen = dateRejected,
			};
			_dbContext.WorkflowHistories.Add(wfh);

			// Notify permit creator: rejected
			var rejCreator = _dbContext.Users.FirstOrDefault(u => u.Id == permit.CreatedBy.ToString());
			if (rejCreator != null)
			{
				var rejPermitNo = string.Format("PTW{0:000000}", permit.RunningNumber);
				var rejLink = $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host.Value}/{permit.Company?.Id}/permits/edit/{permit.Id}";

				_dbContext.Notifications.Add(new Notification
				{
					Title = "Permit Rejected",
					Message = $"{rejPermitNo} has been rejected.",
					Url = rejLink,
					IsRead = false,
					IsArchived = false,
					NotificationUser = rejCreator,
				});

				await _pushService.PushAsync(rejCreator.Id, "Permit Rejected", $"{rejPermitNo} has been rejected.");
			}

			logMessage = $"Permit [PTW{permit.PermitNo}] has been rejected by {fullName.Trim()} ({currentUser.Email}) on {GeneralHelper.GetDateInTimeZone(dateRejected):dd/MM/yyy hh:mm tt}.";
		}

		await _dbContext.SaveChangesAsync();

		await _logService.LogMessageAsync(LogTypeEnum.Information, "PERMIT_APPROVAL", logMessage, currentUser);
	}


	public async Task ClosePermitAsync()
	{
		var form = _httpContextAccessor.HttpContext.Request.Form;
		await ClosePermitAsync(Guid.Parse(form["PermitId"]), form["Notes"]);
	}

	public async Task ClosePermitAsync(Guid permitId, string notes)
	{
		var currentUser = _currentUserService.GetCurrentUser();

		var permit = _dbContext.Permits
			.Include(e => e.PermitWorkflow)
			.Include(e => e.PermitWorkflowStep)
			.Single(e => e.Id == permitId);

		if (permit == null)
		{
			throw new Exception("Permit not found");
		}

		permit.Remarks = notes;
		permit.PermitStatus = PermitStatusEnum.Closed;
		permit.UpdatedBy = Guid.Parse(currentUser.Id);
		permit.UpdatedWhen = DateTime.UtcNow.ToUniversalTime();

		// Create a workflow history entry
		var wfh = new WorkflowHistory
		{
			Permit = permit,
			HistoryWorkflow = permit.PermitWorkflow,
			HistoryWorkflowStep = permit.PermitWorkflowStep,
			Status = WorkflowStatusEnum.Closed,
			Comments = !string.IsNullOrEmpty(notes) ? notes : "Permit has been marked as closed.",
		};

		_dbContext.WorkflowHistories.Add(wfh);
		_dbContext.Permits.Update(permit);

		await _dbContext.SaveChangesAsync();


		// Add audit log to database
		var fullName = $"{currentUser.FirstName} {currentUser.LastName}";
		var logMessage = $"Permit [PTW{permit.PermitNo}] has been marked as closed by ";
		logMessage += $"{fullName.Trim()} on {GeneralHelper.GetDateInTimeZone(DateTime.UtcNow.ToUniversalTime()):dd/MM/yyyy hh:mm tt}.";

		await _logService.LogMessageAsync(LogTypeEnum.Information, "PERMIT_CLOSED", logMessage, currentUser);
	}


	public Permit GetById(Guid entityId)
	{
		return _dbContext.Permits.FirstOrDefault(x => x.Id == entityId);
	}


	public IEnumerable<Permit> GetAll()
	{
		return _dbContext.Permits;
	}


	public IEnumerable<Permit> GetPermitsBySite(Site site)
	{
		var permits = _dbContext.Permits
			.Include(x => x.Site)
			.Where(x => x.Site == site);

		return permits;
	}


	public IEnumerable<Permit> GetPermitsBySite(Guid id)
	{
		var permits = _dbContext.Permits
			.Include(x => x.Site)
			.Where(x => x.Site!.Id == id);

		return permits;
	}
}
