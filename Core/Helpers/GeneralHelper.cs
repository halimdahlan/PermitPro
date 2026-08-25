using PermitPro.Core.Enums;

namespace PermitPro.Core.Helpers;

public static class GeneralHelper
{
	public static DateTime GetDateInTimeZone(DateTime date)
	{
		// On Ubuntu 24.04, this evaluates directly to "Asia/Kuala_Lumpur"
		if (TimeZoneInfo.Local.Id == "Asia/Kuala_Lumpur")
		{
			return date;
		}

		// Fallback: Convert if the server is set to UTC or another timezone
		TimeZoneInfo _tzi = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
		return TimeZoneInfo.ConvertTimeFromUtc(date, _tzi);
	}

	public static string GetCategoryColor(PermitStatusEnum permitStatus)
	{
		var color = "#000000";

		switch (permitStatus)
		{
			case PermitStatusEnum.Approved:
				color = "#00d97e";
				break;
			case PermitStatusEnum.Closed:
				color = "#39afd1";
				break;
			case PermitStatusEnum.Pending:
				color = "#f6c343";
				break;
			case PermitStatusEnum.Rejected:
				color = "#e63757";
				break;
			case PermitStatusEnum.Draft:
				color = "#899bb4";
				break;
		}

		return color;
	}


	public static string GetAlertBgColor(PermitStatusEnum permitStatus)
	{
		var bgColor = "secondary";

		if (permitStatus == PermitStatusEnum.Draft) bgColor = "secondary";
		if (permitStatus == PermitStatusEnum.Pending) bgColor = "secondary";
		if (permitStatus == PermitStatusEnum.Approved) bgColor = "success";
		if (permitStatus == PermitStatusEnum.Rejected) bgColor = "danger";
		if (permitStatus == PermitStatusEnum.Suspended) bgColor = "warning";
		if (permitStatus == PermitStatusEnum.KIV) bgColor = "warning";
		if (permitStatus == PermitStatusEnum.Closed) bgColor = "primary";

		return bgColor;
	}


	public static string GetAlertBgColor(WorkflowStatusEnum workflowStatus)
	{
		var bgColor = "secondary";

		if (workflowStatus == WorkflowStatusEnum.Draft) bgColor = "secondary";
		if (workflowStatus == WorkflowStatusEnum.Pending) bgColor = "secondary";
		if (workflowStatus == WorkflowStatusEnum.Approved) bgColor = "success";
		if (workflowStatus == WorkflowStatusEnum.Rejected) bgColor = "danger";
		if (workflowStatus == WorkflowStatusEnum.Suspended) bgColor = "warning";
		if (workflowStatus == WorkflowStatusEnum.KIV) bgColor = "warning";
		if (workflowStatus == WorkflowStatusEnum.Closed) bgColor = "primary";

		return bgColor;
	}


	public static string GetPermitStatusDisplay(PermitStatusEnum permitStatus, string workflowStepName)
	{
		var status = "";

		if (permitStatus == PermitStatusEnum.Draft) status = "DRAFT";
		if (permitStatus == PermitStatusEnum.Pending) status = $"PENDING FOR APPROVAL - {workflowStepName}";
		if (permitStatus == PermitStatusEnum.Approved) status = "APPROVED";
		if (permitStatus == PermitStatusEnum.Rejected) status = "REJECTED";
		if (permitStatus == PermitStatusEnum.Suspended) status = "SUSPENDED";
		if (permitStatus == PermitStatusEnum.KIV) status = "KIV";
		if (permitStatus == PermitStatusEnum.Closed) status = "CLOSED";

		return status;
	}


	public static string GetStatusBadge(string status)
	{
		var statusBg = "primary";

		if (status == "PENDING" || status == "DRAFT") statusBg = "secondary";
		if (status == "APPROVED") statusBg = "success";
		if (status == "REJECTED") statusBg = "danger";
		if (status == "SUSPENDED" || status == "KIV") statusBg = "warning";
		if (status == "CLOSED") statusBg = "info";

		return $"<span class=\"badge bg-{statusBg}\">{status}</span>";
	}


	public static long FormatDateTimeTicks(DateTime? date)
	{
		long formatted = 0;

		if (date.HasValue)
		{
			if (TimeZoneInfo.Local.Id == "Asia/Kuala_Lumpur")
			{
				formatted = date.Value.Ticks;
			}
			else
			{
				var _tzi = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
				var convertedDate = TimeZoneInfo.ConvertTimeFromUtc(date.Value, _tzi);
				
				formatted = convertedDate.Ticks;
			}
		}

		return formatted;
	}


	public static T ParseEnum<T>(string value)
	{
		return (T)Enum.Parse(typeof(T), value, true);
	}
}
