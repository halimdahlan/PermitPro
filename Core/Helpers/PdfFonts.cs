using QuestPDF.Drawing;

namespace PermitPro.Core.Helpers;

/// <summary>
/// The font used by both permit PDF exporters. QuestPDF doesn't use system fonts, so Roboto
/// (Regular and Bold) ships inside the Core assembly (<c>Resources/Fonts</c>).
/// </summary>
public static class PdfFonts
{
	public const string Family = "Roboto";

	private static readonly object Lock = new();
	private static bool _registered;

	public static void EnsureRegistered()
	{
		lock (Lock)
		{
			if (_registered)
				return;

			var assembly = typeof(PdfFonts).Assembly;
			foreach (var name in new[] { "Fonts.Roboto-Regular.ttf", "Fonts.Roboto-Bold.ttf" })
			{
				using var stream = assembly.GetManifestResourceStream(name)
					?? throw new InvalidOperationException($"Embedded font {name} is missing.");
				FontManager.RegisterFontFromStream(stream);
			}

			_registered = true;
		}
	}
}
