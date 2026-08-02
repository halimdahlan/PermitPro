using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PermitPro.App.Controllers.Base;
using PermitPro.App.ViewModels;
using PermitPro.Core.Data;
using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

namespace PermitPro.App.Controllers;

[Authorize]
public class MaintenanceController : AppControllerBase
{
	private readonly ApplicationDbContext _dbContext;
	private readonly UserManager<UserInfo> _userManager;
	private readonly ICurrentUserService _currentUserService;

	public MaintenanceController(
		ApplicationDbContext dbContext
		, IHttpContextAccessor httpContextAccessor
		, SignInManager<UserInfo> signInManager
		, ISystemConfigurationService systemConfigurationService
		, UserManager<UserInfo> userManager
		, ICurrentUserService currentUserService)
		 : base(dbContext, httpContextAccessor, signInManager, systemConfigurationService)
	{
		_userManager = userManager;
		_dbContext = dbContext;
		_currentUserService = currentUserService;
	}


	[HttpGet("/{company}/maintenance")]
	public async Task<IActionResult> Index(Guid company)
	{
		var permits = _dbContext.Permits
		  .Include(e => e.PermitWorkflowStep)
		  .Where(e => e.PermitWorkflowStep != null)
		  .OrderByDescending(e => e.CreatedWhen);

		var workflowSteps = _dbContext.WorkflowSteps
		  .Include(e => e.WorkflowStepWorkflow)
		  .Where(e => e.WorkflowStepWorkflow != null)
		  .Select(e => new
		  {
			  StepID = e.Id,
			  Workflow = e.WorkflowStepWorkflow,
		  })
		  .ToList();

		try
		{
			foreach (Permit permit in permits)
			{
				var workflowStep = workflowSteps.FirstOrDefault(e => e.StepID == permit.PermitWorkflowStep!.Id);

				if (workflowStep != null)
				{
					if (workflowStep.Workflow != null)
					{
						permit.PermitWorkflow = workflowStep.Workflow;
						_dbContext.Permits.Update(permit);
					}
				}
			}

			await _dbContext.SaveChangesAsync();

			return View(new MaintenanceViewModel
			{
				HasError = false,
				ResultMessage = "Successfully updated permits."
			});
		}
		catch (Exception ex)
		{
			return View(new MaintenanceViewModel
			{
				HasError = true,
				ResultMessage = ex.Message
			});
		}

	}
}