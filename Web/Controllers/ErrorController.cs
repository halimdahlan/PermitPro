#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using PermitPro.App.Models;
using PermitPro.App.Services;
using PermitPro.Core.Helpers;

using System.Diagnostics;

namespace PermitPro.App.Controllers;

[AllowAnonymous]
[Route("error")]
public class ErrorController : Controller
{
    [Route("{statusCode:int?}")]
    public IActionResult Index(int? statusCode)
    {
        var code = statusCode ?? 500;

        var message = code switch
        {
            400 => "The request could not be understood.",
            401 => "You need to be signed in to view this page.",
            403 => "You don't have permission to access this resource.",
            404 => "The page you're looking for doesn't exist.",
            408 => "The request timed out. Please try again.",
            _ => "Something went wrong on our end. Please try again."
        };

        Response.StatusCode = code;

        return View("Error", new ErrorViewModel
        {
            StatusCode = code,
            Message = message,
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

    // Any page on a host whose subdomain isn't an active company (TenantResolutionMiddleware sends them here).
    [Route("company-not-found")]
    public IActionResult CompanyNotFound([FromServices] TenancySettings tenancy)
    {
        Response.StatusCode = StatusCodes.Status404NotFound;

        return View("Error", new ErrorViewModel
        {
            StatusCode = StatusCodes.Status404NotFound,
            Message = $"There's no company at {Request.Host.Host}. Check the address, or find your company's sign-in page.",
            HomeUrl = $"{Request.ApexBaseUrl(tenancy)}/account/login",
        });
    }
}
