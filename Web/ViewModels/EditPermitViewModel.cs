#nullable disable

using PermitPro.Core.Entities;
using PermitPro.Core.Enums;

namespace PermitPro.App.ViewModels;

public record EditPermitViewModel
{
	// ── New-system fields (populated when IsNewSystemPermit = true) ───────────
	public bool IsNewSystemPermit { get; set; }
	public Guid? ActiveTemplateId { get; set; }
	public string LayoutJson { get; set; }
	public string PermitDataJson { get; set; }
	public List<FormControlLibraryItem> ControlLibrary { get; set; } = new();
	public string PermitNo { get; set; }

	public Guid PermitId { get; set; }

	public string PermitJson { get; set; }

	public string WorkflowStepName { get; set; }

	public bool ExecuteWorkflowAction { get; set; }

	public string AlertBgColor { get; set; }

	public PermitStatusEnum PermitStatus { get; set; }

	public string PermitStatusDisplay { get; set; }

	public string PermitCreatedByInfo { get; set; }
	public string PermitUpdatedByInfo { get; set; }

	public Guid CompanyId { get; set; }

	public IList<string> UserRoles { get; set; }

	public IEnumerable<WorkflowHistoryViewModel> WorkflowHistories { get; set; }

	public Site Location { get; set; }

	public DateTime? SuspendDate { get; set; }

	public bool SuspendAutoResume { get; set; }

	public long UploadFileSizeLimit { get; set; }

	public int UploadMaxFileCount { get; set; }

	public string[] UploadAllowedFileTypes { get; set; }
}
