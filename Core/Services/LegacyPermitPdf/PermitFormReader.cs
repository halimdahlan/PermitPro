#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.Globalization;

namespace PermitPro.Core.Services.LegacyPermitPdf;

/// <summary>
/// Null-safe access to an old-system permit's <c>PermitForm</c> JSON for the PDF.
/// Any field may be missing: a certificate that was ticked but never filled in is stored as just
/// <c>{"name":"..."}</c>. Dates are kept as strings while parsing and formatted here, in the app's
/// time zone (the saved values are UTC, e.g. "2024-11-26T16:00:00.000Z").
/// </summary>
internal sealed class PermitFormReader
{
	public const string Dash = "—";

	private static readonly TimeZoneInfo AppTimeZone = FindAppTimeZone();

	private readonly JToken? _root;

	public PermitFormReader(JToken? root) => _root = root;

	public static PermitFormReader Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return new PermitFormReader(null);

		var token = JsonConvert.DeserializeObject<JToken>(json, new JsonSerializerSettings
		{
			DateParseHandling = DateParseHandling.None,
		});

		return new PermitFormReader(token);
	}

	public JToken? Token(string path) => _root?.SelectToken(path);

	public PermitFormReader this[string path] => new(Token(path));

	public bool Exists => _root != null && _root.Type != JTokenType.Null;

	/// <summary>Text value; "—" when missing or blank. Numbers print as-is.</summary>
	public string Text(string path)
	{
		var raw = Raw(path);
		return string.IsNullOrWhiteSpace(raw) ? Dash : raw.Trim();
	}

	/// <summary>"Other" value shown next to a ticked "Other": "(value)", or empty when blank.</summary>
	public string Other(string path)
	{
		var raw = Raw(path);
		return string.IsNullOrWhiteSpace(raw) ? string.Empty : $"({raw.Trim()})";
	}

	/// <summary>Raw string value (null when missing). Objects/arrays are not text and return null.</summary>
	public string? Raw(string path)
	{
		var token = Token(path);
		if (token == null || token.Type is JTokenType.Null or JTokenType.Undefined or JTokenType.Object or JTokenType.Array)
			return null;
		return token.ToString();
	}

	/// <summary>Checkbox/switch value. Accepts true/false or "true"/"false".</summary>
	public bool Bool(string path)
	{
		var token = Token(path);
		if (token == null || token.Type is JTokenType.Null or JTokenType.Undefined)
			return false;
		if (token.Type == JTokenType.Boolean)
			return (bool)token;
		return bool.TryParse(token.ToString(), out var result) && result;
	}

	/// <summary>"YES"/"NO" for a saved boolean, "—" when missing.</summary>
	public string YesNo(string path)
	{
		var token = Token(path);
		if (token == null || token.Type is JTokenType.Null or JTokenType.Undefined)
			return Dash;
		return Bool(path) ? "YES" : "NO";
	}

	public string Date(string path) => FormatDate(path, "dd/MM/yyyy");

	public string Time(string path) => FormatDate(path, "hh:mm tt");

	public string DateTime(string path) => FormatDate(path, "dd/MM/yyyy hh:mm tt");

	private string FormatDate(string path, string format)
	{
		var raw = Raw(path);
		if (string.IsNullOrWhiteSpace(raw))
			return Dash;

		if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
			return raw.Trim();

		var local = TimeZoneInfo.ConvertTime(value, AppTimeZone);
		return local.ToString(format, CultureInfo.InvariantCulture);
	}

	/// <summary>Array items (empty when missing).</summary>
	public IEnumerable<PermitFormReader> Items(string path)
	{
		if (Token(path) is JArray array)
		{
			foreach (var item in array)
				yield return new PermitFormReader(item);
		}
	}

	/// <summary>
	/// The operational-information checkboxes are saved as [{ id: "#chkOpInfo5", status, value }].
	/// Older permits store each id several times; the Edit page applies them in order, so the last
	/// one wins. Returns (status, value) per id without the leading '#'.
	/// </summary>
	public Dictionary<string, (bool Status, string? Value)> StatusById(string path)
	{
		var result = new Dictionary<string, (bool, string?)>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in Items(path))
		{
			var id = item.Raw("id")?.TrimStart('#');
			if (string.IsNullOrEmpty(id))
				continue;
			result[id] = (item.Bool("status"), item.Raw("value"));
		}
		return result;
	}

	private static TimeZoneInfo FindAppTimeZone()
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
		}
		catch (TimeZoneNotFoundException)
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
		}
	}
}
