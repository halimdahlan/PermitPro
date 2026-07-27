#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

using System.Text.Json;

using PermitPro.App.Controllers.Base;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;
using PermitPro.Core.Enums;

using System.Collections;
using System.Text.RegularExpressions;

namespace PermitPro.App.Controllers;

[Authorize]
public class PermitsController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly UserManager<UserInfo> _userManager;
	private readonly ICurrentUserService _currentUserService;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly IPermitService _permitService;
	private readonly IPermitPdfService _permitPdfService;
	private readonly ILogService _logService;
	private readonly ISystemConfigurationService _systemConfigurationService;
	private readonly IFormTemplateService _formTemplates;

	public PermitsController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
		, UserManager<UserInfo> userManager
		, ICurrentUserService currentUserService
		, IWebHostEnvironment webHostEnvironment
		, ILogService logService
		, IPermitService permitService
		, IPermitPdfService permitPdfService
		, IFormTemplateService formTemplates) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_userManager = userManager;
		_dbContext = dbContext;
		_currentUserService = currentUserService;
		_webHostEnvironment = webHostEnvironment;
		_permitService = permitService;
		_permitPdfService = permitPdfService;
		_logService = logService;
		_systemConfigurationService = systemConfigurationService;
		_formTemplates = formTemplates;
	}

	[Route("{company}/permits/removefile")]
	public ActionResult RemoveFile()
	{
		return Content("");
	}


	public async Task<IActionResult> Index(Guid company, string filter, string field)
	{
		var model = new PermitViewModel
		{
			CompanyId = company,
			UserRoles = await _currentUserService.GetCurrentUserRoles(),
			GridFilter = filter,
			GridFilterField = field,
		};

		return View(model);
	}


	[Route("{company}/permits/edit/{id}")]
	public async Task<IActionResult> Edit(Guid company, Guid id, string origin)
	{
		if (!string.IsNullOrEmpty(origin) && origin == "email")
		{
			var returnUrl = $"/{company}/permits/edit/{id}";
			return LocalRedirectPermanent($"/account/login?url={returnUrl}");
		}

		var permit = _dbContext.Permits
			.Include(e => e.Site)
			.Include(e => e.WorkflowHistories)
			.Include(e => e.PermitWorkflowStep)
			.ThenInclude(e => e.Approvers)
			.Where(e => e.Id == id)
			.FirstOrDefault();

		var currentUser = _currentUserService.GetCurrentUser();
		var currentUserIsAdmin = await _userManager.IsInRoleAsync(currentUser, "SuperUser") || await _userManager.IsInRoleAsync(currentUser, "Administrator");

		if (permit != null)
		{
			var workflowHistories = permit.WorkflowHistories
				.OrderByDescending(e => e.CreatedWhen)
				.Select(e => new WorkflowHistoryViewModel
				{
					UserDisplayName = _dbContext.Users
						.Where(u => u.Id.ToLower() == e.CreatedBy.ToString().ToLower())
						.Select(u => new
						{
							FullName = $"{u.FirstName.Trim()} {u.LastName.Trim()}"
						})
						.Single()
						.FullName,
					DateCreated = $"{GeneralHelper.GetDateInTimeZone(e.CreatedWhen):dd MMM, yyyy} at {GeneralHelper.GetDateInTimeZone(e.CreatedWhen):hh:mm tt}",
					Status = e.Status.ToString(),
					Comments = e.Comments,
					StatusBgColor = GeneralHelper.GetAlertBgColor(e.Status),
				})
				.ToList();

			var pattern = @"'";
			var toReplace = @"\'";
			var regExOptions = RegexOptions.Multiline;
			var regEx = new Regex(pattern, regExOptions);

			var permitUpdatedByInfo = "N/A";
			var permitCreatedByInfo = "N/A";

			if (permit.WorkflowHistories.Count > 0)
			{
				var updatedBy = permit.WorkflowHistories
					.OrderByDescending(e => e.CreatedWhen)
					.Select(e => new
					{
						_dbContext.Users
							.Where(u => u.Id.ToLower() == e.CreatedBy.ToString().ToLower())
							.Select(u => new
							{
								FullName = $"{u.FirstName.Trim()} {u.LastName.Trim()}"
							})
							.Single()
							.FullName,
						UpdatedWhen = e.CreatedWhen,
					})
					.FirstOrDefault();

				permitUpdatedByInfo = $"{updatedBy.FullName} on {GeneralHelper.GetDateInTimeZone(updatedBy.UpdatedWhen):dd MMM, yyyy} @ {GeneralHelper.GetDateInTimeZone(updatedBy.UpdatedWhen):hh:mm tt}";
			}

			var createdBy = _dbContext.Users
				.Where(user => user.Id == permit.CreatedBy.ToString())
				.Select(e => new
				{
					UserFullName = $"{e.FirstName} {e.LastName}",
				})
				.FirstOrDefault();

			permitCreatedByInfo = $"{createdBy.UserFullName} on {GeneralHelper.GetDateInTimeZone(permit.CreatedWhen):dd MMM, yyyy} @ {GeneralHelper.GetDateInTimeZone(permit.CreatedWhen):hh:mm tt}";


			var model = new EditPermitViewModel
			{
				PermitNo = $"PTW{permit.RunningNumber:000000}",
				PermitId = permit.Id,
				PermitJson = regEx.Replace(permit.PermitForm, toReplace),
				PermitStatus = permit.PermitStatus,
				PermitStatusDisplay = GeneralHelper.GetPermitStatusDisplay(permit.PermitStatus, permit.PermitWorkflowStep.Name),
				WorkflowStepName = permit.PermitWorkflowStep != null ? permit.PermitWorkflowStep.Name : null,
				AlertBgColor = GeneralHelper.GetAlertBgColor(permit.PermitStatus),
				CompanyId = company,
				UserRoles = _currentUserService.GetCurrentUserRoles().Result,
				WorkflowHistories = permit.WorkflowHistories
					.OrderByDescending(e => e.CreatedWhen)
					.Select(e => new WorkflowHistoryViewModel
					{
						UserDisplayName = _dbContext.Users
							.Where(u => u.Id.ToLower() == e.CreatedBy.ToString().ToLower())
							.Select(u => new
							{
								FullName = $"{u.FirstName.Trim()} {u.LastName.Trim()}"
							})
							.Single()
							.FullName,
						DateCreated = $"{GeneralHelper.GetDateInTimeZone(e.CreatedWhen):dd MMM, yyyy} at {GeneralHelper.GetDateInTimeZone(e.CreatedWhen):hh:mm tt}",
						Status = e.Status.ToString(),
						Comments = e.Comments,
						StatusBgColor = GeneralHelper.GetAlertBgColor(e.Status),
					})
					.ToList(),
				Location = permit.Site,
				SuspendAutoResume = permit.AutoResumeSuspended,
				SuspendDate = permit.SuspendedDateTime,
				PermitUpdatedByInfo = permitUpdatedByInfo,
				PermitCreatedByInfo = permitCreatedByInfo,
				UploadAllowedFileTypes = _systemConfigurationService.UploadAllowedFileTypes.Split(";"),
				UploadFileSizeLimit = _systemConfigurationService.UploadMaxFileSize,
				UploadMaxFileCount = _systemConfigurationService.UploadMaxFileCount
			};

			if (permit.PermitWorkflowStep != null)
			{
				model.ExecuteWorkflowAction = permit.PermitWorkflowStep.Approvers.Any(e => e.Id == currentUser.Id);
			}

			if (currentUserIsAdmin)
			{
				model.ExecuteWorkflowAction = currentUserIsAdmin;
			}

			return View(model);
		}

		return NotFound();
	}


	[Route("{company}/permits/new")]
	public async Task<IActionResult> New(Guid company, CancellationToken ct)
	{
		if (IsNewSystemActive())
			return RedirectToAction(nameof(NewForm), new { company });

		var model = new PermitViewModel
		{
			CompanyId = company,
			EditPermitViewModel = new EditPermitViewModel
			{
				CompanyId = company,
				PermitStatus = PermitStatusEnum.Draft,
				UploadAllowedFileTypes = _systemConfigurationService.UploadAllowedFileTypes.Split(";"),
				UploadFileSizeLimit = _systemConfigurationService.UploadMaxFileSize,
				UploadMaxFileCount = _systemConfigurationService.UploadMaxFileCount
			},
		};

		return View(model);
	}


	[Route("{company}/permits/new/form")]
	public async Task<IActionResult> NewForm(Guid company, CancellationToken ct)
	{
		var template = await _formTemplates.GetActiveAsync(company, Core.Enums.FormTemplateTypeEnum.Permit, ct);
		if (template == null)
		{
			TempData["ErrorMessage"] = "No active permit form template found. Please configure one in Admin → Form Templates.";
			return RedirectToAction(nameof(Index), new { company });
		}

		var controls = await _formTemplates.GetAllAsync(company, Core.Enums.FormTemplateTypeEnum.Permit, ct);
		var model = BuildNewFormViewModel(company, template, null);
		return View(model);
	}


	[Route("{company}/permits/edit/form/{id}")]
	public async Task<IActionResult> EditForm(Guid company, Guid id, CancellationToken ct)
	{
		var permit = await _dbContext.Permits
			.AsNoTracking()
			.Include(p => p.Site)
			.Include(p => p.PermitWorkflowStep).ThenInclude(s => s.Approvers)
			.Include(p => p.WorkflowHistories)
			.FirstOrDefaultAsync(p => p.Id == id, ct);

		if (permit == null) return NotFound();

		if (!IsNewSystemPermit(permit))
			return RedirectToAction(nameof(Edit), new { company, id });

		var templateId = ExtractTemplateIdFromDataJson(permit.PermitDataJson);
		var template = templateId != Guid.Empty
			? await _formTemplates.GetByIdAsync(templateId, ct)
			: await _formTemplates.GetActiveAsync(company, Core.Enums.FormTemplateTypeEnum.Permit, ct);

		if (template == null) return NotFound("Form template not found.");

		var model = BuildNewFormViewModel(company, template, permit);
		return View(model);
	}


	[Route("{company}/permits/formview")]
	public IActionResult FormView()
	{
		return View();
	}


	#region "Grid"

	public async Task<JsonResult> GetGridData(Guid company, string filter)
	{
		var isContractor = await _currentUserService.IsContractor();

		var data = _dbContext.Permits
			.AsNoTracking()
			.Include(e => e.Site)
			.Include(e => e.Attachments)
			.Include(e => e.PermitWorkflowStep)
			.ThenInclude(e => e.WorkflowStepWorkflow)
			.Where(e => e.Company.Id == company)
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new
			{
				e.Id,
				e.PermitForm,
				e.RunningNumber,
				LocationName = e.Site.Name,
				PermitStatus = e.PermitStatus.ToString().ToUpper(),
				e.CreatedWhen,
				e.CreatedBy,
				SubmittedBy = _dbContext.Users
					.Select(p => new
					{
						UserId = p.Id,
						FullName = $"{p.FirstName} {p.LastName}"
					})
					.SingleOrDefault(u => u.UserId == e.CreatedBy.ToString().ToLower())
					.FullName ?? "Unknown",
			});

		if (isContractor)
		{
			var _currentUser = _currentUserService.GetCurrentUser();
			data = data.Where(e => e.CreatedBy.ToString().ToLower() == _currentUser.Id.ToLower());
		}

		var permits = data.ToList();


		List<PermitListViewModel> result = new();

		foreach (var permit in permits)
		{
			using var doc = JsonDocument.Parse(permit.PermitForm);
			var general = doc.RootElement.GetProperty("general");

			var description = general.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
			var dtmStart = general.TryGetProperty("startDateTime", out var s) && s.ValueKind != JsonValueKind.Null ? s.GetString() : null;
			var dtmEnd   = general.TryGetProperty("endDateTime",   out var en) && en.ValueKind != JsonValueKind.Null ? en.GetString() : null;

			List<string> certs = new();
			if (general.TryGetProperty("certificates", out var certsEl) && certsEl.ValueKind == JsonValueKind.Array)
			{
				foreach (var cert in certsEl.EnumerateArray())
				{
					if (!cert.TryGetProperty("name", out var nameEl)) continue;
					var certName = nameEl.GetString() ?? "";

					CertificateIcon selectedCert = certName.ToLower() switch
					{
						"hotwork"         => new() { Code = "A", Description = "Hot Work" },
						"confinedspace"   => new() { Code = "B", Description = "Confined Space" },
						"radiation"       => new() { Code = "C", Description = "Radiation" },
						"excavation"      => new() { Code = "D", Description = "Excavation" },
						"isolation"       => new() { Code = "E", Description = "Isolation" },
						"methodstatement" => new() { Code = "F", Description = "Method Statement" },
						"liftinghoisting" => new() { Code = "G", Description = "Lifting & Hoisting" },
						"override"        => new() { Code = "H", Description = "Override" },
						_                 => new() { Code = "X", Description = certName },
					};

					certs.Add($"<img title=\"({selectedCert.Code}) {selectedCert.Description}\" src=\"/img/icons/certs/{certName.ToLower()}.png\" style=\"width:32px;height:32px;margin-right:3px;\" />");
				}
			}

			var newPermit = new PermitListViewModel
			{
				Id = permit.Id,
				PermitNumber = string.Format("PTW{0:000000}", permit.RunningNumber),
				Description = GetTruncatedWords(description, 20),
				Location = permit.LocationName,
				StartDate = !string.IsNullOrEmpty(dtmStart) ? DateTime.Parse(dtmStart).ToLocalTime() : null,
				EndDate   = !string.IsNullOrEmpty(dtmEnd)   ? DateTime.Parse(dtmEnd).ToLocalTime()   : null,
				Certificates = certs.Count > 0 ? "<div class=\"d-flex flex-row certificate-icons justify-content-center\">" + string.Join("", certs) + "</div>" : "",
				Status = permit.PermitStatus,
				StatusBadge = GeneralHelper.GetStatusBadge(permit.PermitStatus.ToUpper()),
				DateSubmitted = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen),
				SubmittedBy = permit.SubmittedBy,
			};

			result.Add(newPermit);
		}

		return new JsonResult(result, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}


	public JsonResult GetPermitStatusAll()
	{
		List<SelectListItem> list = new()
		{
			new SelectListItem
			{
				Text = "(select)",
				Value = "",
			},
			new SelectListItem
			{
				Text = "Draft",
				Value = "DRAFT",
			},
			new SelectListItem
			{
				Text = "Pending",
				Value = "PENDING",
			},
			new SelectListItem
			{
				Text = "Approved",
				Value = "APPROVED",
			},
			new SelectListItem
			{
				Text = "Rejected",
				Value = "REJECTED",
			},
			new SelectListItem
			{
				Text = "Closed",
				Value = "CLOSED",
			},
			new SelectListItem
			{
				Text = "Suspended",
				Value = "SUSPENDED",
			},
			new SelectListItem
			{
				Text = "Overdue",
				Value = "OVERDUE",
			},
		};

		return Json(list.ToList());
	}

	#endregion


	#region "GET"

	[HttpGet("{company}/permits/permit/{id}/files")]
	public IActionResult GetUploadedFiles(Guid company, Guid id)
	{
		var files = _dbContext.Attachments
			.AsNoTracking()
			.Include(f => f.Permit)
			.Where(f => f.Permit.Id == id)
			.OrderByDescending(f => f.CreatedWhen)
			.Select(f => new
			{
				PermitId = f.Permit.Id,
				f.Id,
				f.FileName,
				FileSize = $"{(f.FileSize / 1024):n0}",
				f.ContentType,
				CreatedWhen = GeneralHelper.GetDateInTimeZone(f.CreatedWhen).ToString("dd MMM, yyyy"),
				CreatedWhenTick = GeneralHelper.FormatDateTimeTicks(GeneralHelper.GetDateInTimeZone(f.CreatedWhen)),
			})
			.ToList();

		return Ok(new
		{
			Data = files,
		});
	}

	#endregion


	#region "POST"

	[HttpPost("{company}/permits")]
	public async Task<IActionResult> CreatePermit(Guid company)
	{
		try
		{
			await _permitService.CreateAsync();

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}



	[HttpPost("{company}/permits/permit/status")]
	public async Task<IActionResult> PermitStatus()
	{
		await _permitService.SetPermitStatusAsync();

		return Ok();
	}


	[HttpPost("{company}/permits/{id}/suspend")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SuspendPermit(Guid id)
	{
		try
		{
			var req = Request.Form;
			DateTime? suspendDate = null;
			PermitStatusEnum permitStatus = PermitStatusEnum.Pending;
			PermitStatusEnum? previousPermitStatus = null;
			bool suspendAutoResume = false;
			var logMessage = "";

			var suspendState = req["SuspendState"].ToString();

			var permit = _dbContext.Permits.FirstOrDefault(e => e.Id == id);

			if (permit == null)
			{
				await _logService.LogMessageAsync(LogTypeEnum.Error, "PERMIT_SUSPEND", "Permit not found", _currentUserService.GetCurrentUser());
				return NotFound("Permit not found");
			}

			if (suspendState == "suspend")
			{
				if (string.IsNullOrEmpty(req["SuspendDate"]))
				{
					await _logService.LogMessageAsync(LogTypeEnum.Error, "PERMIT_SUSPEND", "Suspend date is required", _currentUserService.GetCurrentUser());
					return BadRequest("Suspend date is required");
				}

				using var tmpDate = JsonDocument.Parse(req["SuspendDate"]);
				var dateRoot = tmpDate.RootElement;

				suspendDate = new DateTime(
					dateRoot.GetProperty("year").GetInt32(),
					dateRoot.GetProperty("month").GetInt32(),
					dateRoot.GetProperty("day").GetInt32(),
					0, 0, 0);

				suspendAutoResume = Convert.ToBoolean(req["SuspendAutoResume"]);
				permitStatus = PermitStatusEnum.Suspended;
				previousPermitStatus = permit.PermitStatus;
				logMessage = $"Permit PTW{permit.RunningNumber:000000} has been suspended";
			}
			else
			{
				logMessage = $"Permit PTW{permit.RunningNumber:000000} suspension has been cancelled";
				permitStatus = permit.PreviousPermitStatus ?? PermitStatusEnum.Pending;
				previousPermitStatus = null;
			}

			permit.SuspendedDateTime = suspendDate;
			permit.PermitStatus = permitStatus;
			permit.PreviousPermitStatus = previousPermitStatus;
			permit.AutoResumeSuspended = suspendAutoResume;
			permit.UpdatedWhen = DateTime.Now.ToUniversalTime();
			permit.UpdatedBy = Guid.Parse(_currentUserService.GetCurrentUser().Id);

			_dbContext.Permits.Update(permit);
			await _dbContext.SaveChangesAsync();

			await _logService.LogMessageAsync(LogTypeEnum.Error, "PERMIT_SUSPEND", logMessage, _currentUserService.GetCurrentUser());

			return Ok(new
			{
				Status = "OK",
			});
		}
		catch (Exception ex)
		{
			await _logService.LogMessageAsync(LogTypeEnum.Error, "PERMIT_SUSPEND", ex.Message, _currentUserService.GetCurrentUser());
			return BadRequest(ex.Message);
		}

	}


	[HttpPost("{company}/permits/attachments/remove")]
	public async Task<IActionResult> RemoveAttachment()
	{
		var req = Request.Form;

		try
		{
			var attachmentId = Guid.Parse(req["FileId"]);
			var attachment = _dbContext.Attachments
				.Include(e => e.Permit)
				.FirstOrDefault(e => e.Id == attachmentId);

			if (attachment != null)
			{
				_dbContext.Attachments.Remove(attachment);
				await _dbContext.SaveChangesAsync();

				// Delete physical file from server
				var filePath = Path.Combine(_webHostEnvironment.WebRootPath, "attachments", "permits", attachment.Permit.Id.ToString().ToLower(), attachment.FileName);

				if (System.IO.File.Exists(filePath))
				{
					System.IO.File.Delete(filePath);
				}
			}

			return Ok(new
			{
				Status = "OK",
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}

	}

	#endregion


	#region "PUT"

	[HttpPut("{company}/permits")]
	public async Task<IActionResult> UpdatePermit(Guid company)
	{
		var req = Request.Form;

		try
		{
			await _permitService.UpdateAsync();

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}

	}


	[HttpPut("{company}/permits/certs")]
	public async Task<IActionResult> UpdatePermitCertificate(Guid company)
	{
		var req = Request.Form;

		try
		{
			await _permitService.UpdateCertificateAsync();

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}

	}


	[HttpPut("{company}/permits/closed")]
	public async Task<IActionResult> MarkPermitAsClosed()
	{
		try
		{
			await _permitService.ClosePermitAsync();

			return Ok(new
			{
				Result = true,
			});
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}

	#endregion


	#region "DELETE"

	[HttpDelete("{company}/permits/{id}")]
	public async Task<IActionResult> DeletePermit(Guid company, Guid id)
	{
		try
		{
			var permit = _dbContext.Permits.FirstOrDefault(e => e.Id == id);
			await _permitService.DeleteAsync(permit);

			return Ok(new
			{
				Data = "OK"
			});
		}
		catch (Exception ex)
		{
			return BadRequest(new
			{
				ex.Message
			});
		}
	}

	#endregion


	#region "PDF"

	[HttpGet("{company}/permits/templates/permit")]
	public IActionResult GetPermitHtmlTemplate()
	{
		var htmlTemplate = System.IO.File.ReadAllText(_webHostEnvironment.WebRootPath + "/templates/html/permit-main-div-only.html");

		return Ok(new
		{
			Data = htmlTemplate
		});
	}


	[HttpPost("{company}/permits/permit/export/pdf")]
	public async Task<IActionResult> ExportPdf(Guid company)
	{
		var pdf = await _permitPdfService.GetPdfBytesAsync(Request.Form);
		return File(pdf, "application/pdf", $"{Guid.NewGuid()}.pdf");
	}


	// ── New-system CRUD ───────────────────────────────────────────────────────

	[HttpPost("{company}/permits/form")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SaveNewPermit(Guid company, string permitDataJson, Guid templateId, string submissionType, CancellationToken ct)
	{
		try
		{
			var permitId = await _permitService.CreateNewSystemAsync(
				company, templateId, permitDataJson, submissionType ?? "draft", Request.Form.Files, ct);

			return Ok(new { permitId });
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}


	[HttpPut("{company}/permits/form/{id}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> UpdateNewPermit(Guid company, Guid id, string permitDataJson, string submissionType, CancellationToken ct)
	{
		try
		{
			await _permitService.UpdateNewSystemAsync(
				id, company, permitDataJson, submissionType ?? "draft", Request.Form.Files, ct);

			return Ok(new { permitId = id });
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}

	#endregion


	#region "Private static functions/methods"

	private string GetTruncatedWords(string text, int wordCount)
	{
		if (string.IsNullOrWhiteSpace(text)) return text;

		// Split by whitespace and remove any empty entries
		var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

		// If the text is shorter than the limit, return it as-is
		if (words.Length <= wordCount) return text;

		// Join the first N words and append ellipsis
		return string.Join(" ", words.Take(wordCount)) + " ...";
	}


	private Hashtable GetCertificateIcons()
	{
		Hashtable ht = new();
		ht.Add("hotwork", new CertificateIcon { Code = "A", Description = "Hot Work" });
		ht.Add("confinedspace", new CertificateIcon { Code = "B", Description = "Confined Space" });
		ht.Add("radiation", new CertificateIcon { Code = "C", Description = "Radiation" });
		ht.Add("excavation", new CertificateIcon { Code = "D", Description = "Excavation" });
		ht.Add("isolation", new CertificateIcon { Code = "E", Description = "Isolation" });
		ht.Add("methodStatement", new CertificateIcon { Code = "F", Description = "Method Statement" });
		ht.Add("liftingHoisting", new CertificateIcon { Code = "G", Description = "Lifting & Hoisting" });
		ht.Add("override", new CertificateIcon { Code = "H", Description = "Override" });
		return ht;
	}


	private record CertificateIcon
	{
		public string Code { get; init; }
		public string Description { get; init; }
	}

	private bool IsNewSystemActive()
		=> _systemConfigurationService.NewPermitFormCutoverDate.HasValue
		   && DateTime.UtcNow >= _systemConfigurationService.NewPermitFormCutoverDate.Value;

	private bool IsNewSystemPermit(Permit permit)
		=> _systemConfigurationService.NewPermitFormCutoverDate.HasValue
		   && permit.CreatedWhen >= _systemConfigurationService.NewPermitFormCutoverDate.Value;

	private static Guid ExtractTemplateIdFromDataJson(string permitDataJson)
	{
		if (string.IsNullOrWhiteSpace(permitDataJson)) return Guid.Empty;
		try
		{
			using var doc = JsonDocument.Parse(permitDataJson);
			if (doc.RootElement.TryGetProperty("templateId", out var el) && el.TryGetGuid(out var id))
				return id;
		}
		catch { }
		return Guid.Empty;
	}

	private EditPermitViewModel BuildNewFormViewModel(Guid companyId, Core.Entities.FormTemplate template, Permit permit)
	{
		// Enrich LayoutJson: embed full control data into each column slot so the
		// view script can render each field without a separate API call.
		var enrichedLayout = EnrichLayout(template.LayoutJson, companyId);

		return new EditPermitViewModel
		{
			IsNewSystemPermit = true,
			PermitId = permit?.Id ?? Guid.Empty,
			PermitNo = permit != null ? $"PTW{permit.RunningNumber:000000}" : "(new)",
			PermitStatus = permit?.PermitStatus ?? PermitStatusEnum.Draft,
			PermitStatusDisplay = permit != null
				? GeneralHelper.GetPermitStatusDisplay(permit.PermitStatus, permit.PermitWorkflowStep?.Name)
				: "Draft",
			AlertBgColor = permit != null ? GeneralHelper.GetAlertBgColor(permit.PermitStatus) : "secondary",
			CompanyId = companyId,
			ActiveTemplateId = template.Id,
			LayoutJson = enrichedLayout,
			PermitDataJson = permit?.PermitDataJson,
			UserRoles = _currentUserService.GetCurrentUserRoles().Result,
			WorkflowHistories = permit?.WorkflowHistories?
				.OrderByDescending(h => h.CreatedWhen)
				.Select(h => new WorkflowHistoryViewModel
				{
					UserDisplayName = _dbContext.Users
						.Where(u => u.Id.ToLower() == h.CreatedBy.ToString().ToLower())
						.Select(u => $"{u.FirstName} {u.LastName}")
						.FirstOrDefault() ?? "Unknown",
					DateCreated = $"{GeneralHelper.GetDateInTimeZone(h.CreatedWhen):dd MMM, yyyy} at {GeneralHelper.GetDateInTimeZone(h.CreatedWhen):hh:mm tt}",
					Status = h.Status.ToString(),
					Comments = h.Comments,
					StatusBgColor = GeneralHelper.GetAlertBgColor(h.Status),
				})
				.ToList() ?? new(),
			UploadAllowedFileTypes = _systemConfigurationService.UploadAllowedFileTypes.Split(";"),
			UploadFileSizeLimit = _systemConfigurationService.UploadMaxFileSize,
			UploadMaxFileCount = _systemConfigurationService.UploadMaxFileCount,
		};
	}

	private string EnrichLayout(string layoutJson, Guid companyId)
	{
		try
		{
			using var doc = JsonDocument.Parse(layoutJson);
			var root = doc.RootElement;

			// Collect all distinct controlIds referenced in the layout
			var controlIds = root.GetProperty("rows").EnumerateArray()
				.SelectMany(r => r.GetProperty("columns").EnumerateArray())
				.Where(c => c.TryGetProperty("controlId", out var cid) && cid.ValueKind != JsonValueKind.Null)
				.Select(c => { c.GetProperty("controlId").TryGetGuid(out var id); return id; })
				.Where(id => id != Guid.Empty)
				.Distinct()
				.ToList();

			var controls = _dbContext.FormControls
				.AsNoTracking()
				.Where(c => c.CompanyId == companyId && controlIds.Contains(c.Id))
				.Select(c => new
				{
					c.Id, c.Name, c.Label, ControlType = (int)c.ControlType,
					c.IsRequired, c.Placeholder, c.HelpText, c.OptionsJson,
				})
				.ToList();

			var controlMap = controls.ToDictionary(c => c.Id);

			// Rebuild layout JSON with embedded control data
			var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

			var enriched = new
			{
				rows = root.GetProperty("rows").EnumerateArray().Select(row => new
				{
					id = row.GetProperty("id").GetString(),
					columnCount = row.GetProperty("columnCount").GetInt32(),
					columns = row.GetProperty("columns").EnumerateArray().Select(col =>
					{
						Guid? ctrlId = null;
						if (col.TryGetProperty("controlId", out var cidEl) && cidEl.ValueKind != JsonValueKind.Null)
							cidEl.TryGetGuid(out var tmpId);

						col.TryGetProperty("controlId", out var cidEl2);
						Guid parsedId = Guid.Empty;
						if (cidEl2.ValueKind != JsonValueKind.Null) cidEl2.TryGetGuid(out parsedId);

						object control = null;
						if (parsedId != Guid.Empty && controlMap.TryGetValue(parsedId, out var ctrl))
							control = ctrl;

						return new
						{
							id = col.GetProperty("id").GetString(),
							controlId = parsedId == Guid.Empty ? (Guid?)null : parsedId,
							labelOverride = col.TryGetProperty("labelOverride", out var lo) && lo.ValueKind != JsonValueKind.Null ? lo.GetString() : null,
							isRequiredOverride = col.TryGetProperty("isRequiredOverride", out var iro) && iro.ValueKind == JsonValueKind.True ? (bool?)true :
												 iro.ValueKind == JsonValueKind.False ? (bool?)false : null,
							control,
						};
					}).ToList(),
				}).ToList(),
			};

			return JsonSerializer.Serialize(enriched, opts);
		}
		catch
		{
			return layoutJson;
		}
	}

	#endregion

}
