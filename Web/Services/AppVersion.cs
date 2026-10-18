using System.Reflection;

namespace PermitPro.App.Services;

/// <summary>
/// The running app's version, from <c>&lt;Version&gt;</c> in <c>App.csproj</c> (or <c>-p:Version=…</c> at publish),
/// as Major.Minor.Patch, e.g. "1.2.1".
/// </summary>
public static class AppVersion
{
	public static string Current { get; } = Read();

	private static string Read()
	{
		var version = typeof(AppVersion).Assembly.GetName().Version;
		return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
	}
}
