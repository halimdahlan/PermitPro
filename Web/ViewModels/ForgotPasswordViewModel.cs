using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class ForgotPasswordViewModel
{
	[Required(ErrorMessage = "This field is required")]
	[EmailAddress(ErrorMessage = "Please enter a valid email address")]
	[DataType(DataType.EmailAddress)]
	[Display(Name = "Email address")]
	public required string Email { get; set; }

	public bool EmailSent { get; set; }

	public string? ErrorMessage { get; set; } = string.Empty;

	public string? Message { get; set; } = string.Empty;

}
