#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Controllers.Base;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Enums;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using System.Text.Json;
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
	private readonly IQuestPdfService _questPdfService;
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
		, IQuestPdfService questPdfService
		, IFormTemplateService formTemplates) : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_userManager = userManager;
		_dbContext = dbContext;
		_currentUserService = currentUserService;
		_webHostEnvironment = webHostEnvironment;
		_permitService = permitService;
		_permitPdfService = permitPdfService;
		_questPdfService = questPdfService;
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
			StatusOptions = GetPermitStatusOptions(),
		};

		return View(model);
	}


	[Route("{company}/permits/edit/{id}")]
	public async Task<IActionResult> Edit(Guid company, Guid id, string origin, CancellationToken ct = default)
	{
		if (!string.IsNullOrEmpty(origin) && origin == "email")
		{
			var returnUrl = $"/{company}/permits/edit/{id}";
			return LocalRedirectPermanent($"/account/login?url={returnUrl}");
		}

		// New-system permits use a different view — redirect immediately.
		var permitCheck = _dbContext.Permits.AsNoTracking()
			.Where(p => p.Id == id)
			.Select(p => new { p.CreatedWhen })
			.FirstOrDefault();

		if (permitCheck != null && _systemConfigurationService.NewPermitFormCutoverDate.HasValue
			&& permitCheck.CreatedWhen >= _systemConfigurationService.NewPermitFormCutoverDate.Value)
			return RedirectToAction(nameof(EditForm), new { company, id });

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
				// Raw JSON; the view encodes it as a JavaScript string.
				PermitJson = permit.PermitForm ?? string.Empty,
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
		var allTemplates = await _formTemplates.GetAllAsync(company, Core.Enums.FormTemplateTypeEnum.Permit, ct);
		var template = allTemplates.FirstOrDefault();
		if (template == null)
		{
			TempData["ErrorMessage"] = "No permit form template found. Please configure one in Admin → Form Templates.";
			return RedirectToAction(nameof(Index), new { company });
		}
		var model = BuildNewFormViewModel(company, template, null, false, null);
		return View(model);
	}


	[Route("{company}/permits/edit/form/{id}")]
	public async Task<IActionResult> EditForm(Guid company, Guid id, CancellationToken ct)
	{
		var currentUser = _currentUserService.GetCurrentUser();
		var currentUserIsAdmin = await _userManager.IsInRoleAsync(currentUser, "Super User") || await _userManager.IsInRoleAsync(currentUser, "Administrator");

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
			: (await _formTemplates.GetAllAsync(company, Core.Enums.FormTemplateTypeEnum.Permit, ct)).FirstOrDefault();

		if (template == null) return NotFound("Form template not found.");

		var model = BuildNewFormViewModel(company, template, permit, currentUserIsAdmin, currentUser.Id);
		return View(model);
	}


	#region "Grid"

	public async Task<JsonResult> GetGridData(Guid company, string filter, CancellationToken ct)
	{
		var isContractor = await _currentUserService.IsContractor();

		IQueryable<Permit> query = _dbContext.Permits
			.AsNoTracking()
			.Where(e => e.CompanyId == company);

		if (isContractor)
		{
			var currentUserId = _currentUserService.GetCurrentUser().Id.ToLower();
			query = query.Where(e => e.CreatedBy.ToString().ToLower() == currentUserId);
		}

		//var sw = System.Diagnostics.Stopwatch.StartNew();

		// Exclude PermitForm — it's an nvarchar(max) LOB column; SQL Server reads its pages
		// even inside a CASE expression, so it must be kept out of this query entirely.
		var permits = await query
			.OrderByDescending(e => e.CreatedWhen)
			.Select(e => new
			{
				e.Id,
				e.Title,
				e.Description,
				e.StartDateTime,
				e.EndDateTime,
				e.RunningNumber,
				e.PermitForm,
				LocationName = e.Site != null ? e.Site.Name : "",
				PermitStatus = e.PermitStatus.ToString().ToUpper(),
				e.CreatedWhen,
				CreatedById = e.CreatedBy.ToString().ToLower(),
			})
			.ToListAsync(ct);

		//sw.Stop();
		//Console.WriteLine($"[GetGridData] permits query: {sw.ElapsedMilliseconds}ms ({permits.Count} rows)");

		// Second fetch: only pull PermitForm for legacy rows that need it (Description not yet backfilled).
		// var legacyIds = permits.Where(p => p.Description == null).Select(p => p.Id).ToList();
		// var legacyForms = legacyIds.Count == 0
		// 	? new Dictionary<Guid, string>()
		// 	: await _dbContext.Permits
		// 		.Where(e => legacyIds.Contains(e.Id))
		// 		.Select(e => new { e.Id, e.PermitForm })
		// 		.ToDictionaryAsync(e => e.Id, e => e.PermitForm, ct);

		// Batch-fetch all referenced users in one query instead of a correlated subquery per row
		var createdByIds = permits.Select(p => p.CreatedById).Distinct().ToList();
		var userNames = await _dbContext.Users
			.Where(u => createdByIds.Contains(u.Id.ToLower()))
			.Select(u => new { UserId = u.Id.ToLower(), FullName = u.FirstName + " " + u.LastName })
			.ToDictionaryAsync(u => u.UserId, u => u.FullName, ct);

		List<PermitListViewModel> result = new();

		foreach (var permit in permits)
		{
			// Title/Description: prefer DB columns (populated by both systems).
			// Fall back to parsing PermitForm JSON only for legacy rows that predate
			// the migration backfill (i.e. Title is still null).
			string description = permit.Description ?? "";
			DateTime? dtmStart = permit.StartDateTime;
			DateTime? dtmEnd = permit.EndDateTime;
			List<string> certs = new();

			//legacyForms.TryGetValue(permit.Id, out var permitForm);
			string permitForm = permit.PermitForm ?? "";

			if (!string.IsNullOrEmpty(permitForm))
			{
				//try
				//{
					using var doc = JsonDocument.Parse(permitForm);

					var general = doc.RootElement.GetProperty("general");
					description = general.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";

					var sStr = general.TryGetProperty("startDateTime", out var s) && s.ValueKind != JsonValueKind.Null ? s.GetString() : null;
					var eStr = general.TryGetProperty("endDateTime", out var en) && en.ValueKind != JsonValueKind.Null ? en.GetString() : null;

					if (sStr != null) dtmStart = DateTime.Parse(sStr).ToLocalTime();
					if (eStr != null) dtmEnd = DateTime.Parse(eStr).ToLocalTime();

					if (general.TryGetProperty("certificates", out var certsEl) && certsEl.ValueKind == JsonValueKind.Array)
					{
						foreach (var cert in certsEl.EnumerateArray())
						{
							if (!cert.TryGetProperty("name", out var nameEl)) continue;
							var certName = nameEl.GetString() ?? "";
							CertificateIcon selectedCert = certName.ToLower() switch
							{
								"hotwork" => new() { Code = "A", Description = "Hot Work" },
								"confinedspace" => new() { Code = "B", Description = "Confined Space" },
								"radiation" => new() { Code = "C", Description = "Radiation" },
								"excavation" => new() { Code = "D", Description = "Excavation" },
								"isolation" => new() { Code = "E", Description = "Isolation" },
								"methodstatement" => new() { Code = "F", Description = "Method Statement" },
								"liftinghoisting" => new() { Code = "G", Description = "Lifting & Hoisting" },
								"override" => new() { Code = "H", Description = "Override" },
								_ => new() { Code = "X", Description = certName },
							};
							certs.Add($"<img title=\"({selectedCert.Code}) {selectedCert.Description}\" src=\"/img/icons/certs/{certName.ToLower()}.png\" style=\"width:32px;height:32px;margin-right:3px;\" />");
						}
					}
				//}
				//catch { /* malformed JSON — leave defaults */ }
			}

			result.Add(new PermitListViewModel
			{
				Id = permit.Id,
				PermitNumber = string.Format("PTW{0:000000}", permit.RunningNumber),
				Description = GetTruncatedWords(description, 20),
				Location = permit.LocationName,
				StartDate = dtmStart,
				EndDate = dtmEnd,
				Certificates = certs.Count > 0
					? "<div class=\"d-flex flex-row certificate-icons justify-content-center\">" + string.Join("", certs) + "</div>"
					: "",
				Status = permit.PermitStatus,
				StatusBadge = GeneralHelper.GetStatusBadge(permit.PermitStatus.ToUpper()),
				DateSubmitted = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen),
				SubmittedBy = userNames.GetValueOrDefault(permit.CreatedById, "Unknown"),
			});
		}

		return new JsonResult(result, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
		});
	}


	// Options for the status filter on the permit list. Values match PermitListViewModel.Status.
	private static List<SelectListItem> GetPermitStatusOptions()
	{
		return new()
		{
			new SelectListItem
			{
				Text = "All statuses",
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
	}

	#endregion


	#region "GET"

	[HttpGet("{company}/permits/permit/{id}/files")]
	public JsonResult GetUploadedFiles(Guid company, Guid id)
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

		return new JsonResult(files, new JsonSerializerOptions
		{
			PropertyNamingPolicy = null,
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


	[HttpPost("{company}/permits/{id}/attachments/upload")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> UploadAttachment(Guid company, Guid id, IFormFileCollection files, CancellationToken ct)
	{
		var permit = await _dbContext.Permits.FindAsync([id], ct);
		if (permit is null) return NotFound();

		var maxSize  = _systemConfigurationService.UploadMaxFileSize;
		var maxCount = _systemConfigurationService.UploadMaxFileCount;
		var allowed  = (_systemConfigurationService.UploadAllowedFileTypes ?? "")
						.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
						.Select(e => e.Trim().TrimStart('.').ToLower())
						.ToHashSet();

		var added = new List<object>();

		foreach (var file in files.Where(f => f.Length > 0))
		{
			var ext      = Path.GetExtension(file.FileName).TrimStart('.').ToLower();
			var safeName = Path.GetFileName(file.FileName);
			if (string.IsNullOrWhiteSpace(safeName)) continue;
			if (allowed.Count > 0 && !allowed.Contains(ext)) continue;
			if (file.Length > maxSize) continue;

			var existing = await _dbContext.Attachments.CountAsync(a => a.Permit.Id == id, ct);
			if (existing >= maxCount) break;

			var folder = Path.Combine(_webHostEnvironment.WebRootPath, "attachments", "permits", id.ToString().ToLower());
			Directory.CreateDirectory(folder);

			var filePath = Path.Combine(folder, safeName);
			await using (var fs = new FileStream(filePath, FileMode.Create))
				await file.CopyToAsync(fs, ct);

			var fi = new FileInfo(filePath);

			if (!_dbContext.Attachments.Any(a => a.FileName == safeName && a.Permit.Id == id))
			{
				var attachment = new Attachment
				{
					FileName    = safeName,
					ContentType = ext,
					Permit      = permit,
					FileSize    = (int)fi.Length,
				};
				_dbContext.Attachments.Add(attachment);
				await _dbContext.SaveChangesAsync(ct);

				added.Add(new
				{
					attachment.Id,
					PermitId    = id,
					attachment.FileName,
					FileSize    = $"{(attachment.FileSize / 1024):n0}",
					attachment.ContentType,
					CreatedWhen = GeneralHelper.GetDateInTimeZone(attachment.CreatedWhen).ToString("dd MMM, yyyy"),
				});
			}
		}

		return Ok(new { Data = added });
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

	[HttpPost("{company}/permits/permit/export/pdf")]
	public async Task<IActionResult> ExportPdf(Guid company, CancellationToken ct)
	{
		if (!Guid.TryParse(Request.Form["Id"], out var permitId))
			return BadRequest("Invalid permit ID.");

		var permitCheck = await _dbContext.Permits
			.AsNoTracking()
			.Include(p => p.Company)
			.Include(p => p.Site)
			.Include(p => p.PermitWorkflowStep)
			.Include(p => p.WorkflowHistories)
			.Include(p => p.Certificates.Where(c => !c.IsDeleted)).ThenInclude(c => c.FormTemplate)
			.FirstOrDefaultAsync(p => p.Id == permitId, ct);

		if (permitCheck == null) return NotFound();

		byte[] pdf;

		if (_systemConfigurationService.NewPermitFormCutoverDate.HasValue
			&& permitCheck.CreatedWhen >= _systemConfigurationService.NewPermitFormCutoverDate.Value)
		{
			pdf = await _questPdfService.GenerateAsync(permitCheck, ct);
		}
		else
		{
			var legacyPdf = await _permitPdfService.GenerateAsync(permitId, ct);
			if (legacyPdf == null) return NotFound();
			pdf = legacyPdf;
		}

		return File(pdf, "application/pdf", $"PTW{permitCheck.RunningNumber:000000}.pdf");
	}


	// ── New-system CRUD ───────────────────────────────────────────────────────

	[HttpPost("{company}/permits/form")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SaveNewPermit(Guid company, string description, string startDateTime, string endDateTime, string permitDataJson, Guid templateId, string submissionType, IFormFileCollection permitFiles, CancellationToken ct)
	{
		try
		{
			DateTime? start = DateTime.TryParse(startDateTime, out var s) ? s : null;
			DateTime? end   = DateTime.TryParse(endDateTime,   out var e) ? e : null;

			var permitId = await _permitService.CreateNewSystemAsync(
				company, templateId, description, start, end, permitDataJson, submissionType ?? "draft", permitFiles, ct);

			return Ok(new { permitId });
		}
		catch (Exception ex)
		{
			return BadRequest(ex.Message);
		}
	}


	[HttpPut("{company}/permits/form/{id}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> UpdateNewPermit(Guid company, Guid id, string description, string startDateTime, string endDateTime, string permitDataJson, string submissionType, IFormFileCollection permitFiles, CancellationToken ct)
	{
		try
		{
			DateTime? start = DateTime.TryParse(startDateTime, out var s) ? s : null;
			DateTime? end   = DateTime.TryParse(endDateTime,   out var e) ? e : null;

			await _permitService.UpdateNewSystemAsync(
				id, company, description, start, end, permitDataJson, submissionType ?? "draft", permitFiles, ct);

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

	private EditPermitViewModel BuildNewFormViewModel(Guid companyId, Core.Entities.FormTemplate template, Permit permit, bool currentUserIsAdmin, string currentUserId)
	{
		// Enrich LayoutJson: embed full control data into each column slot so the
		// view script can render each field without a separate API call.
		var enrichedLayout = EnrichLayout(template.LayoutJson, companyId);
		var model = new EditPermitViewModel
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
			PermitDescription = permit?.Description,
			PermitStartDateTime = permit?.StartDateTime?.ToLocalTime(),
			PermitEndDateTime = permit?.EndDateTime?.ToLocalTime(),
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
			ExecuteWorkflowAction = false,
			PermitCreatedByInfo = permit == null ? null : BuildUserDateInfo(permit.CreatedBy, permit.CreatedWhen),
			PermitUpdatedByInfo = permit?.UpdatedWhen == null ? null : BuildUserDateInfo(permit.UpdatedBy, permit.UpdatedWhen.Value),
		};

		if (currentUserIsAdmin)
		{
			model.ExecuteWorkflowAction = true;
		}
		else
		{
			if (permit != null && permit.PermitWorkflowStep != null)
			{
				if (!string.IsNullOrEmpty(currentUserId))
				{
					model.ExecuteWorkflowAction = permit.PermitWorkflowStep.Approvers.Any(e => e.Id == currentUserId);
				}
			}
		}

		return model;
	}

	private string BuildUserDateInfo(Guid? userId, DateTime when)
	{
		var name = userId == null ? "Unknown" :
			_dbContext.Users
				.Where(u => u.Id == userId.Value.ToString().ToLower())
				.Select(u => $"{u.FirstName} {u.LastName}")
				.FirstOrDefault() ?? "Unknown";
		return $"{name} on {GeneralHelper.GetDateInTimeZone(when):dd MMM, yyyy} at {GeneralHelper.GetDateInTimeZone(when):hh:mm tt}";
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
					c.Id,
					c.Name,
					c.Label,
					ControlType = (int)c.ControlType,
					c.IsRequired,
					c.Placeholder,
					c.HelpText,
					c.OptionsJson,
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
						//Guid? ctrlId = null;

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
