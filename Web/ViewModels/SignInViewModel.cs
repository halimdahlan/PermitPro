using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class SignInViewModel
{
	[Required(ErrorMessage = "This field is required")]
	[EmailAddress(ErrorMessage = "Enter a valid email address")]
	[DataType(DataType.EmailAddress)]
	[Display(Name = "Email address")]
	public required string Email { get; set; }

	[Required(ErrorMessage = "This field is required")]
	[MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
	[DataType(DataType.Password)]
	[Display(Name = "Password")]
	public required string Password { get; set; }

	[Display(Name = "Remember me")]
	public bool RememberMe { get; set; }

	public string? Token { get; set; }

	// Where to go after signing in (local URLs only), e.g. a permit opened from a workflow email.
	public string? ReturnUrl { get; set; }
}


// The apex's sign-in page: the company's address, e.g. "iotm" + ".permitpro.app".
public class CompanyAddressViewModel
{
	public string? Subdomain { get; set; }

	public required string HostSuffix { get; set; }

	public string? ErrorMessage { get; set; }
}
