#nullable disable

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PermitPro.Core.Entities;

public class AppSetting
{
    [Key]
    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public AppSettingCategory Category { get; set; }

    public Guid? CompanyId { get; set; }

    [Required]
    public string Key { get; set; }

    [Required]
    public string DisplayName { get; set; }

    public string Value { get; set; }

    /// <summary>text | password | number | email | boolean</summary>
    [Required]
    public string DataType { get; set; } = "text";

    public bool IsEncrypted { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// When true (company-scoped rows only), the effective value resolves to the global
    /// (CompanyId == null) row for this key instead of this row's own Value.
    /// </summary>
    public bool IsUsingGlobal { get; set; }
}
