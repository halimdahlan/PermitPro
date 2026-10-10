using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PermitPro.Core.Helpers;

/// <summary>
/// The logo printed in a permit PDF header: the company's uploaded logo (<c>wwwroot/img/logos</c>,
/// PNG/JPG/WebP/SVG), or the default <c>wwwroot/img/app-logo.jpg</c> when the company has none or its
/// file is missing or unreadable.
/// </summary>
public sealed class PdfLogo
{
	private readonly Image? _image;
	private readonly SvgImage? _svg;

	private PdfLogo(Image? image, SvgImage? svg)
	{
		_image = image;
		_svg = svg;
	}

	public static PdfLogo? Load(string webRootPath, string? companyLogoFileName)
	{
		if (!string.IsNullOrWhiteSpace(companyLogoFileName))
		{
			// GetFileName: the stored name is a GUID + extension; never follow a path out of the logos folder.
			var logo = TryLoad(Path.Combine(webRootPath, "img", "logos", Path.GetFileName(companyLogoFileName)));
			if (logo != null)
				return logo;
		}

		return TryLoad(Path.Combine(webRootPath, "img", "app-logo.jpg"));
	}

	private static PdfLogo? TryLoad(string path)
	{
		if (!File.Exists(path))
			return null;

		try
		{
			return string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase)
				? new PdfLogo(null, SvgImage.FromText(File.ReadAllText(path)))
				: new PdfLogo(Image.FromBinaryData(File.ReadAllBytes(path)), null);
		}
		catch (Exception)
		{
			return null;   // corrupt or unsupported file: fall back rather than fail the export
		}
	}

	/// <summary>Draws the logo scaled to fit the container, keeping its aspect ratio.</summary>
	public void Compose(IContainer container)
	{
		if (_svg != null)
			container.Svg(_svg).FitArea();
		else
			container.Image(_image!).FitArea();
	}
}
