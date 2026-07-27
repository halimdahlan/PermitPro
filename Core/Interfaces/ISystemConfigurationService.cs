
using PermitPro.Core.Entities;

namespace PermitPro.Core.Interfaces;

public interface ISystemConfigurationService
{
	IEnumerable<SystemMenu> AuthorizedMenus { get; }

	IEnumerable<string> ReservedRoles { get; }

	string ApplicationDomain { get; }

	int UserCreateLimit { get; }

	int UploadMaxFileSize { get; }

	int UploadMaxFileCount { get; }

	string UploadAllowedFileTypes { get; }

	string SMTPServer { get; }

	int SMTPPort { get; }

	string SenderName { get; }

	string SenderEmail { get; }

	string EmailUserName { get; }

	string EmailPassword { get; }

	int SuspendedAutoResume { get; }

	DateTime? NewPermitFormCutoverDate { get; }

	void Init();

	bool HasAccess(string controller);

}
