using System.ComponentModel.DataAnnotations;

namespace PermitPro.App.ViewModels;

public class ManageUserMainViewModel
{
	public ManageUserViewNodel? UserInfoForm { get; set; }
	public ManageUserPasswordViewModel? UserPasswordForm { get; set; }
}

public class ManageUserViewNodel
{
	public string Id { get; set; } = string.Empty;

	[Required(ErrorMessage = "First name is required")]
	public required string FirstName { get; set; }

	[Required(ErrorMessage = "Last name is required")]
	public required string LastName { get; set; }

	[Required(ErrorMessage = "Email is required")]
	[EmailAddress]
	public required string Email { get; set; }

	public string? Designation { get; set; } = string.Empty;

	[Required(ErrorMessage = "Role is required")]
	public required string UserRole { get; set; }

	public bool IsActive { get; set; }

	public string? UserPassword { get; set; }

	public bool IsEdit { get; set; } = false;

	public bool SetPasswordOnly { get; set; } = false;

	public Guid CompanyID { get; set; }

	public List<RoleDropDown> Roles { get; set; } = new();

	public string? Locations { get; set; } = string.Empty;

	public string ReadOnly() => IsEdit ? " readonly" : "";

	public int MaxNumOfUsers { get; set; }

	public bool HasExceededLimit { get; set; }

	public bool NoRolesAvailable { get; set; }

	public bool OriginFromContractors { get; set; }

	public string DefaultContractorRole { get; set; }

}


public class NewUserViewModel : ManageUserViewNodel
{
	[Required(ErrorMessage = "New password is required")]
	[MinLength(8, ErrorMessage = "Mininum length is 8 characters")]
	[DataType(DataType.Password)]
	public string? NewPassword { get; set; } = string.Empty;

	[Required(ErrorMessage = "Confirm password is required")]
	[MinLength(8, ErrorMessage = "Mininum length is 8 characters")]
	[DataType(DataType.Password)]
	[Compare("NewPassword", ErrorMessage = "The passwords do not match.")]
	public string? ConfirmPassword { get; set; } = string.Empty;
}


public class RoleDropDown
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public bool IsUnlimitedUsers { get; set; }
}


public class UserSites
{
	public Guid Id { get; set; }
}


public class ManageUserPasswordViewModel
{
	[Required(ErrorMessage = "New password is required")]
	[MinLength(8, ErrorMessage = "Mininum length is 8 characters")]
	[DataType(DataType.Password)]
	public required string NewPassword { get; set; } = string.Empty;

	[Required(ErrorMessage = "Confirm password is required")]
	[MinLength(8, ErrorMessage = "Mininum length is 8 characters")]
	[DataType(DataType.Password)]
	[Compare("NewPassword", ErrorMessage = "The passwords do not match.")]
	public required string ConfirmPassword { get; set; } = string.Empty;

	public bool IsContractors { get; set; }
}


public class ManageUserPasswordAdminViewModel
{
	[Required(ErrorMessage = "Password is required")]
	[MinLength(8, ErrorMessage = "Mininum length is 8 characters")]
	[DataType(DataType.Password)]
	public required string Password { get; set; }

	[Required(ErrorMessage = "Confirm password is required")]
	[MinLength(8, ErrorMessage = "Mininum length is 8 characters")]
	[DataType(DataType.Password)]
	[Compare("Password", ErrorMessage = "The passwords do not match.")]
	public required string ConfirmPassword { get; set; }

	public bool OriginFromContractors { get; set; }
}