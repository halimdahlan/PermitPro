using PermitPro.Core.Enums;

using System.ComponentModel.DataAnnotations;

namespace PermitPro.Core.Entities;

public class AuditLog
{
	[Key]
	public Guid Id { get; set; }

	public string? EntityName { get; set; }

	public string? Category { get; set; }

	public string? Description { get; set; }

	public string? Url { get; set; }

	public LogTypeEnum LogType { get; set; }

	public string? SerializedData { get; set; }

	public Guid? CompanyId { get; set; }

	public UserInfo? AuditLogUser { get; set; }

	public DateTime CreatedWhen { get; set; }

	public DateTime? UpdatedWhen { get; set; }

	public Guid? CreatedBy { get; set; }

	public Guid? UpdatedBy { get; set; }
}
