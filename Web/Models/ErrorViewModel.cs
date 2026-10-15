namespace PermitPro.App.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public int StatusCode { get; set; } = 500;
    public string Message { get; set; } = "Something went wrong on our end. Please try again.";

    // Where the Home button goes; the apex's company address page when the company itself wasn't found.
    public string HomeUrl { get; set; } = "/";

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
