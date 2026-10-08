using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class ResetPasswordViewModel
{
	// Length matches the Identity policy (RequiredLength = 12 in WebExtensions.AddIdentityConfiguration);
	// Identity itself enforces the character rules and reports them via the validation summary.
	[DataType(DataType.Password)]
	[StringLength(128, MinimumLength = 12, ErrorMessage = "Password must be at least 12 characters long")]
	[Required(ErrorMessage = "Please enter your password")]
	[Display(Name = "Password")]
	public required string Password { get; set; }

	[DataType(DataType.Password)]
	[Required(ErrorMessage = "Please confirm your password")]
	[Compare("Password", ErrorMessage = "Password does not match")]
	[Display(Name = "Confirm Password")]
	public required string ConfirmPassword { get; set; }

	public string? AdditionalMessage { get; set; } = string.Empty;

	public string? Id { get; set; } = string.Empty;

	public string? ResetToken { get; set; } = string.Empty;

	public string? ErrorMessage { get; set; } = string.Empty;

	public string? Message { get; set; } = string.Empty;

	public bool ResetOK { get; set; }

}
