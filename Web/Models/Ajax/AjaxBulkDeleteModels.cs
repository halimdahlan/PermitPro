namespace PermitPro.App.Models.Ajax;

/// <summary>Grid "Delete selected" (DataGrid.bulkDelete in datagrid.js): the ids of the ticked rows.</summary>
public class AjaxBulkDeleteRequest
{
	public List<string> Ids { get; set; } = new();
}

/// <summary>How many were deleted, and which were skipped and why (shown in the grid's result dialog).</summary>
public class AjaxBulkDeleteResult
{
	public int Deleted { get; set; }

	public List<AjaxBulkDeleteFailure> Failed { get; set; } = new();

	public void Fail(string id, string name, string reason) => Failed.Add(new AjaxBulkDeleteFailure { Id = id, Name = name, Reason = reason });
}

public class AjaxBulkDeleteFailure
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Reason { get; set; } = string.Empty;
}
