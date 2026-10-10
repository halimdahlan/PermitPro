#nullable enable

using PermitPro.Core.Helpers;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PermitPro.Core.Services.LegacyPermitPdf;

/// <summary>
/// Old-system permit PDF (permits created before the new-form cutover), drawn with QuestPDF.
/// Reproduces the layout of the former Puppeteer/HTML export: three main-form pages, then one
/// section per attached certificate (A–H). Sizes follow the old HTML, where 1 CSS px printed as
/// 0.683 pt (a 750 px wide form scaled to the A4 content width).
/// </summary>
internal sealed partial class LegacyPermitPdfDocument : IDocument
{
	// ── Look (measured from the former export) ──────────────────────────────
	private const float PxToPt = 0.683f;
	private const float BorderWidth = 0.68f;
	private const float FontSize = 8.2f;
	private const float CheckSize = 13.7f;

	private const string TextColor = "#212529";
	private const string ValueColor = "#4682b4";   // steelblue
	private const string BorderColor = "#acb4bd";
	private const string HeadingFill = "#e2e3e4";
	private const string RuleColor = "#c8c9ca";     // Bootstrap <hr>

	private const float SpaceWidth = 2.03f;   // one Roboto space at 8.2 pt

	private static float Px(float px) => px * PxToPt;

	private readonly LegacyPermitPdfModel _model;
	private readonly PermitFormReader _form;

	public LegacyPermitPdfDocument(LegacyPermitPdfModel model)
	{
		_model = model;
		_form = model.Form;
	}

	public DocumentMetadata GetMetadata() => new()
	{
		Title = $"Permit {_model.PermitNo}",
		Author = _model.CompanyName,
	};

	public void Compose(IDocumentContainer container)
	{
		container.Page(page =>
		{
			page.Size(PageSizes.A4);
			page.MarginLeft(42f);
			page.MarginRight(41.7f);
			page.MarginTop(15f);
			page.MarginBottom(14.5f);
			page.DefaultTextStyle(x => x.FontFamily(PdfFonts.Family).FontSize(FontSize).FontColor(TextColor).LineHeight(1.5f));

			page.Header().Element(ComposeHeader);
			page.Content().Element(ComposeContent);
			page.Footer().Element(ComposeFooter);
		});
	}

	// ── Header / footer ─────────────────────────────────────────────────────

	private void ComposeHeader(IContainer container)
	{
		// Logo and company name start at x = 153.8 pt, as before; the form text starts where it did.
		container.Height(54.8f).PaddingLeft(153.8f - 42f).Row(row =>
		{
			if (_model.Logo != null)
				row.ConstantItem(42f).Height(26.2f).Element(_model.Logo.Compose);

			row.ConstantItem(7.8f);
			row.AutoItem().Column(col =>
			{
				col.Item().Text(_model.CompanyName.ToUpperInvariant()).Bold().FontSize(13f).FontColor(Colors.Black).LineHeight(1.22f);
				col.Item().Text("PERMIT TO WORK FORM").FontSize(10f).FontColor(Colors.Black).LineHeight(1.1f);
			});
		});
	}

	private static void ComposeFooter(IContainer container)
	{
		// Page number sits right of the content box, as before (right edge at x = 573.5 pt).
		container.PaddingTop(5f).OffsetX(19.3f).AlignRight().Text(text =>
		{
			text.DefaultTextStyle(x => x.FontSize(8f).FontColor(Colors.Black).LineHeight(1.1f));
			text.CurrentPageNumber();
			text.Span("/");
			text.TotalPages();
		});
	}

	private void ComposeContent(IContainer container)
	{
		container.Column(col =>
		{
			// The old form's outer "mt-2" margin only applied to the first page.
			col.Item().PaddingTop(Px(8)).Element(MainFormPage1);
			col.Item().PageBreak();
			col.Item().Element(MainFormPage2);
			col.Item().PageBreak();
			col.Item().Element(MainFormPage3);

			foreach (var cert in _model.Certificates)
			{
				col.Item().PageBreak();
				col.Item().Element(c => ComposeCertificate(c, cert));
			}
		});
	}

	// ── Building blocks ─────────────────────────────────────────────────────

	/// <summary>
	/// Border on the given edges. In CSS a 1px border takes up space, in QuestPDF it doesn't, so the
	/// border's width is added as padding to keep the old layout's spacing.
	/// </summary>
	private static IContainer Edges(IContainer c, bool top = false, bool right = false, bool bottom = false, bool left = false)
	{
		if (top) c = c.BorderTop(BorderWidth);
		if (right) c = c.BorderRight(BorderWidth);
		if (bottom) c = c.BorderBottom(BorderWidth);
		if (left) c = c.BorderLeft(BorderWidth);

		return c.BorderColor(BorderColor)
			.PaddingTop(top ? BorderWidth : 0).PaddingRight(right ? BorderWidth : 0)
			.PaddingBottom(bottom ? BorderWidth : 0).PaddingLeft(left ? BorderWidth : 0);
	}

	private static IContainer Bordered(IContainer c) => Edges(c, true, true, true, true);

	/// <summary>Grey section bar with a bold title (Bootstrap "px-2 py-1 bg-secondary-subtle").</summary>
	private static void Heading(IContainer c, string title, bool top = true, bool bottom = true)
	{
		Edges(c, top: top, bottom: bottom).Background(HeadingFill).PaddingHorizontal(Px(8)).PaddingVertical(Px(4)).Text(title).Bold();
	}

	private static IContainer Pad(IContainer c, float x = 8, float y = 4) => c.PaddingHorizontal(Px(x)).PaddingVertical(Px(y));

	private void Check(IContainer c, bool isChecked) =>
		c.Width(CheckSize).Height(CheckSize).Image(isChecked ? _model.CheckMark : _model.CheckEmpty).FitArea();

	/// <summary>
	/// Checkbox, gap, label (Bootstrap "d-flex align-items-center" with "me-2").
	/// <paramref name="inline"/>: size to the label (for use inside an auto-sized row cell, where a
	/// relative-width label has no width to fill and the layout never completes).
	/// </summary>
	private void CheckLabel(IContainer c, bool isChecked, string label, string? value = null, bool alignTop = false, bool inline = false)
	{
		c.AlignTop().Row(row =>
		{
			var box = row.ConstantItem(CheckSize);
			(alignTop ? box.PaddingTop(1f) : box.AlignMiddle()).Element(x => Check(x, isChecked));
			row.ConstantItem(Px(8));
			(inline ? row.AutoItem() : row.RelativeItem()).AlignMiddle().Text(text =>
			{
				text.Span(string.IsNullOrEmpty(value) ? label : label + " ");
				if (!string.IsNullOrEmpty(value))
					text.Span(value).FontColor(ValueColor);
			});
		});
	}

	/// <summary>Label on the left, checkbox on the right ("d-flex justify-content-between").</summary>
	private void LabelCheck(IContainer c, string label, bool isChecked, string? value = null)
	{
		c.AlignTop().Row(row =>
		{
			row.RelativeItem().AlignMiddle().Text(text =>
			{
				text.Span(string.IsNullOrEmpty(value) ? label : label + " ");
				if (!string.IsNullOrEmpty(value))
					text.Span(value).FontColor(ValueColor);
			});
			row.ConstantItem(CheckSize).AlignMiddle().Element(x => Check(x, isChecked));
		});
	}

	/// <summary>Label, 8px gap, value in blue (Bootstrap "me-2" between them).</summary>
	private static void LabelGapValue(IContainer c, string label, string value)
	{
		c.Row(row =>
		{
			row.AutoItem().Text(label);
			row.ConstantItem(Px(8) + SpaceWidth);   // the margin plus the space between the HTML elements
			row.AutoItem().Text(value).FontColor(ValueColor);
		});
	}

	/// <summary>"Label: value" on one line; the value in blue.</summary>
	private static void LabelValue(IContainer c, string label, string value, bool boldValue = false)
	{
		c.Text(text =>
		{
			text.Span(label);
			var span = text.Span(value).FontColor(ValueColor);
			if (boldValue) span.Bold();
		});
	}

	/// <summary>Label on one line, value (possibly multi-line) below it in blue.</summary>
	private static void LabelAbove(IContainer c, string label, string value)
	{
		c.Column(col =>
		{
			col.Item().Text(label);
			col.Item().Text(value).FontColor(ValueColor);
		});
	}

	/// <summary>Bootstrap &lt;hr&gt;: a light line with margins above and below (px).</summary>
	private static void Rule(IContainer c, float marginPx = 5) => Rule(c, marginPx, marginPx);

	private static void Rule(IContainer c, float topPx, float bottomPx) =>
		c.PaddingTop(Px(topPx)).PaddingBottom(Px(bottomPx)).LineHorizontal(BorderWidth).LineColor(RuleColor);

	// ── Main form: page 1 ───────────────────────────────────────────────────

	private void MainFormPage1(IContainer container)
	{
		var activities = _form.StatusById("operation.activities");
		var locations = _form.StatusById("operation.activityLocations");
		var tools = _form.StatusById("operation.activityTools");
		var products = _form.StatusById("operation.activityProducts");

		bool On(Dictionary<string, (bool Status, string? Value)> list, string id) => list.TryGetValue(id, out var v) && v.Status;
		string? Val(Dictionary<string, (bool Status, string? Value)> list, string id) =>
			list.TryGetValue(id, out var v) && !string.IsNullOrWhiteSpace(v.Value) ? v.Value!.Trim() : null;

		container.Column(col =>
		{
			// Date issued / permit number
			col.Item().Element(Bordered).Element(c => Pad(c)).Row(row =>
			{
				row.RelativeItem().Text(t => { t.Span("Date Issued: "); t.Span(_model.DateIssued).Bold(); });
				row.AutoItem().Text(t => { t.Span("NO: "); t.Span(_model.PermitNo).Bold(); });
			});

			col.Item().Element(b_ => Edges(b_, left: true, right: true, bottom: true)).Column(box =>
			{
				box.Item().Element(c => Heading(c, "GENERAL INFORMATION", top: false));

				box.Item().Element(b_ => Edges(b_, bottom: true)).Element(c => Pad(c)).Row(row =>
				{
					row.AutoItem().Text("Location:");
					row.ConstantItem(Px(16));
					row.RelativeItem().Text(_form.Text("general.location.name")).FontColor(ValueColor);
				});

				// Description of work | Appendix — associated certificates
				box.Item().Row(row =>
				{
					row.RelativeItem().Element(c => Pad(c)).Column(c =>
					{
						c.Item().PaddingBottom(Px(8)).Text("Description of work:");
						c.Item().Text(_form.Text("general.description")).FontColor(ValueColor);
					});

					// 245 px of columns plus the panel's own left border.
					row.ConstantItem(Px(245) + BorderWidth).Element(b_ => Edges(b_, left: true)).Column(appendix =>
					{
						appendix.Item().Element(b_ => Edges(b_, bottom: true)).Padding(Px(8)).AlignCenter()
							.Text("Appendix — Associated Certificate");

						appendix.Item().Row(head =>
						{
							head.ConstantItem(Px(145)).Padding(Px(4)).Text(" ");
							head.ConstantItem(Px(50)).Element(b_ => Edges(b_, left: true)).Padding(Px(4)).AlignCenter().Text("YES");
							head.ConstantItem(Px(50)).Element(b_ => Edges(b_, left: true)).Padding(Px(4)).AlignCenter().Text("NO");
						});

						foreach (var cert in _model.CertificateChecklist)
						{
							appendix.Item().Element(b_ => Edges(b_, top: true)).Row(r =>
							{
								r.ConstantItem(Px(25)).Padding(Px(4)).AlignCenter().AlignMiddle().Text(cert.Letter);
								r.ConstantItem(Px(120)).Element(b_ => Edges(b_, left: true)).Padding(Px(4)).AlignMiddle().Text(cert.Title);
								r.ConstantItem(Px(50)).Element(b_ => Edges(b_, left: true)).Padding(Px(4)).AlignCenter().AlignMiddle()
									.Element(x => Check(x, cert.Attached));
								r.ConstantItem(Px(50)).Element(b_ => Edges(b_, left: true)).Padding(Px(4)).AlignCenter().AlignMiddle()
									.Element(x => Check(x, !cert.Attached));
							});
						}
					});
				});

				box.Item().Element(c => Heading(c, "PERMIT HOLDER INFORMATION"));

				// Inlined: laid out like the old flex row, but long names wrap onto a second line instead of overflowing.
				box.Item().Element(c => Pad(c)).Inlined(line =>
				{
					line.HorizontalSpacing(Px(96));
					line.Item().Element(c => LabelGapValue(c, "Name:", _form.Text("permitHolder.name")));
					line.Item().Element(c => LabelGapValue(c, "Company:", _form.Text("permitHolder.company")));
					line.Item().Element(c => LabelGapValue(c, "Number of staff:", _form.Text("permitHolder.numOfStaff")));
				});

				box.Item().Element(b_ => Edges(b_, top: true)).Element(c => Pad(c, 8, 8)).Column(c =>
				{
					c.Item().Text("Additional Staff");
					c.Item().Text(_form.Text("permitHolder.additionalStaff"));
				});

				box.Item().Element(b_ => Edges(b_, top: true)).PaddingVertical(Px(8)).Column(c =>
				{
					c.Item().AlignCenter().Text("INSTRUCTIONS FOR HOLDER").Bold();
					c.Item().AlignCenter().Text("Work Must be performed according to ALL safety instructions.");
					c.Item().AlignCenter().Text("Mobile phones are strictly prohibited from the terminal area.");
				});

				box.Item().Element(c => Heading(c, "OPERATIONAL INFORMATION"));

				box.Item().Padding(Px(8)).PaddingBottom(Px(16)).Column(op =>
				{
					op.Item().PaddingBottom(Px(4)).Text("What are the activities?");
					op.Item().Row(row =>
					{
						// Column order as on the old form (ids chkOpInfo1..19).
						string[][] columns =
						{
							new[] { "1:Excavation", "2:Entering Confined Space", "3:Assemble", "4:Disassemble", "5:Prefabrication" },
							new[] { "9:Cutting", "6:Opening Confined Space", "7:Welding", "8:Work at Height", "14:Lifting/Crane Activities" },
							new[] { "10:Drilling/Grinding", "11:High-Pressure Jetting", "12:Grit Blasting", "13:X-Ray/Radiography", "19:Other" },
							new[] { "15:Calibration", "16:Electrical Activities", "17:Nitrogen Pressure Test", "18:Hydrostatic Test" },
						};

						foreach (var items in columns)
						{
							row.RelativeItem().Column(c =>
							{
								foreach (var item in items)
								{
									var parts = item.Split(':', 2);
									var id = "chkOpInfo" + parts[0];
									c.Item().Element(x => CheckLabel(x, On(activities, id), parts[1], parts[0] == "19" ? Val(activities, id) : null));
								}
							});
						}
					});

					op.Item().Element(c => Rule(c));
					op.Item().PaddingBottom(Px(4)).Text("Where are the activities?");
					op.Item().Row(row =>
					{
						foreach (var (id, label) in new[] { ("chkOpInfo20", "Zone 0"), ("chkOpInfo22", "Zone 1"), ("chkOpInfo23", "Zone 2"), ("chkOpInfo21", "Unclassified") })
							row.RelativeItem().Element(x => CheckLabel(x, On(locations, id), label));
					});

					op.Item().Element(c => Rule(c));
					op.Item().PaddingBottom(Px(4)).Text("With what?");
					op.Item().Row(row =>
					{
						row.RelativeItem().Element(x => CheckLabel(x, On(tools, "chkOpInfo24"), "Explosion Proof/Intrisically Safe", alignTop: true));
						row.RelativeItem().Element(x => CheckLabel(x, On(tools, "chkOpInfo25"), "Spark-free Tools"));
						row.RelativeItem().Element(x => CheckLabel(x, On(tools, "chkOpInfo26"), "Lifting Equipment/Crane"));
						row.RelativeItem().Element(x => CheckLabel(x, On(tools, "chkOpInfo27"), "Scaffold"));
					});
					op.Item().Element(x => CheckLabel(x, On(tools, "chkOpInfo27a"), "Other", Val(tools, "chkOpInfo27a")));

					op.Item().Element(c => Rule(c));
					op.Item().PaddingBottom(Px(4)).Text("Product involved - specify product contained in system");
					op.Item().Row(row =>
					{
						row.RelativeItem().Element(x => CheckLabel(x, On(products, "chkOpInfo28"), "Class 1"));
						row.RelativeItem().Element(x => CheckLabel(x, On(products, "chkOpInfo29"), "Class 2"));
						row.RelativeItem().Element(x => CheckLabel(x, On(products, "chkOpInfo30"), "Class 3"));
						row.RelativeItem().Element(x => CheckLabel(x, On(products, "chkOpInfo31"), "Other", Val(products, "chkOpInfo31")));
					});
				});
			});
		});
	}

	// ── Main form: page 2 ───────────────────────────────────────────────────

	private void MainFormPage2(IContainer container)
	{
		var mech = _form["operationMeasures.mechanical"];
		var elec = _form["operationMeasures.electrical"];
		var inst = _form["operationMeasures.instrument"];
		var ppe = _form["ppe"];
		var area = _form["workArea"];
		var pre = _form["precautions"];

		container.Column(col =>
		{
			col.Item().Element(b_ => Edges(b_, left: true, right: true, bottom: true)).Column(box =>
			{
				box.Item().Element(c => Heading(c, "OPERATIONAL MEASURES"));

				box.Item().Row(row =>
				{
					// Mechanical
					row.RelativeItem().Element(b_ => Edges(b_, right: true)).Column(c =>
					{
						c.Item().Element(x => MeasureTitle(x, "MECHANICAL"));
						c.Item().Element(x => MeasureField(x, "Equipment:", mech.Text("equipmentName")));
						c.Item().Element(x => MeasureField(x, "ID/Tag Number:", mech.Text("tagNumber")));
						c.Item().PaddingHorizontal(Px(8)).PaddingTop(Px(4)).PaddingBottom(Px(8)).Column(list =>
						{
							list.Item().Element(x => LabelCheck(x, "Pressure free", mech.Bool("pressureFree.status")));
							list.Item().Element(x => LabelCheck(x, "Empty", mech.Bool("empty.status")));
							list.Item().Element(x => LabelCheck(x, "Disconnected", mech.Bool("disconnected.status")));
							list.Item().Element(x => LabelCheck(x, "Blank off", mech.Bool("blankOff.status")));
							list.Item().Element(x => LabelCheck(x, "Locked off", mech.Bool("lockedOff.status")));
							list.Item().Element(x => LabelCheck(x, "Flushed", mech.Bool("flushed.status")));
							list.Item().Element(x => LabelCheck(x, "Ventilated", mech.Bool("ventilated.status")));
							list.Item().Element(x => LabelCheck(x, "Other", mech.Bool("other.status"), mech.Other("other.value")));
						});
					});

					// Electrical
					row.RelativeItem().Element(b_ => Edges(b_, right: true)).Column(c =>
					{
						c.Item().Element(x => MeasureTitle(x, "ELECTRICAL"));
						c.Item().Element(x => MeasureField(x, "Equipment:", elec.Text("equipmentName")));
						c.Item().Element(x => MeasureField(x, "ID/Tag Number:", elec.Text("tagNumber")));
						c.Item().PaddingHorizontal(Px(8)).PaddingTop(Px(4)).Column(list =>
						{
							list.Item().Element(x => LabelCheck(x, "Locked off", elec.Bool("lockedOff.status")));
							list.Item().Element(x => LabelCheck(x, "Isolated", elec.Bool("isolated.status")));
							list.Item().Element(x => LabelCheck(x, "See Isolation Certificate", elec.Bool("isolationCertificate.status")));
							list.Item().Element(x => LabelNumber(x, "No. of Isolation Certificates", elec.Text("numOfIsolationCertificate")));
						});
					});

					// Instrument < 24V
					row.RelativeItem().Column(c =>
					{
						c.Item().Element(x => MeasureTitle(x, "INSTRUMENT <24V"));
						c.Item().Element(x => MeasureField(x, "Equipment:", inst.Text("equipmentName")));
						c.Item().Element(x => MeasureField(x, "ID/Tag Number:", inst.Text("tagNumber")));
						c.Item().PaddingHorizontal(Px(8)).PaddingTop(Px(4)).Column(list =>
						{
							list.Item().Element(x => LabelCheck(x, "Locked off", inst.Bool("lockedOff.status")));
							list.Item().Element(x => LabelCheck(x, "Isolated", inst.Bool("isolated.status")));
							list.Item().Element(x => LabelCheck(x, "Disconnected", inst.Bool("disconnected.status")));
							list.Item().Element(x => LabelCheck(x, "Bypass", inst.Bool("bypass.status")));
							list.Item().Element(x => LabelNumber(x, "No. of Isolation Certificates", inst.Text("numOfIsolationCertificate")));
						});
					});
				});

				box.Item().Element(c => Heading(c, "OPERATIONAL MEASURES (continued)"));

				box.Item().Row(row =>
				{
					row.RelativeItem().Element(b_ => Edges(b_, right: true)).PaddingHorizontal(Px(8)).PaddingTop(Px(4)).Column(c =>
					{
						c.Item().Element(x => LabelCheck(x, "Shutdown system operational?", inst.Bool("shutdownSystemOperational.status")));
						c.Item().Element(x => LabelCheck(x, "Fire protection system operational?", inst.Bool("fireProtectionSysOperational.status")));
						c.Item().PaddingBottom(Px(8)).Element(x => LabelCheck(x, "Automatic fire protection system operational?", inst.Bool("autoFireProtectionSysOperational.status")));
						c.Item().PaddingBottom(Px(4)).Text("PTW HANDOVER TO HOLDER").Bold();
						c.Item().Element(x => LabelCheck(x, "Is aware of the safety instructions as in the Terminal Safety Manual?", _form.Bool("operationMeasures.pwtHandOver.terminalSafetyAwareness")));
						c.Item().PaddingBottom(Px(8)).Element(x => LabelCheck(x, "Is aware of the safety instructions as in the Permit to Work\nand associated certificates?", _form.Bool("operationMeasures.pwtHandOver.ptwAwareness")));
					});
					row.RelativeItem().PaddingHorizontal(Px(8)).PaddingTop(Px(4))
						.Element(c => LabelAbove(c, "Any other measures?", inst.Text("otherMeasures")));
				});
			});

			// PPE | Work area
			col.Item().Element(b_ => Edges(b_, left: true, right: true, bottom: true)).Row(row =>
			{
				row.RelativeItem().Element(b_ => Edges(b_, right: true)).Column(c =>
				{
					c.Item().Element(x => Heading(x, "PPE", top: false));
					c.Item().Element(x => Pad(x)).Row(r =>
					{
						r.RelativeItem().PaddingRight(Px(8)).Column(l =>
						{
							l.Item().Element(x => LabelCheck(x, "Safety Glasses", ppe.Bool("safetyGlass.status")));
							l.Item().Element(x => LabelCheck(x, "Safety Helmet", ppe.Bool("safetyHelmet.status")));
							l.Item().Element(x => LabelCheck(x, "Full Face Mask", ppe.Bool("fullFaceMask.status")));
							l.Item().Element(x => LabelCheck(x, "Leather Gloves", ppe.Bool("leatherGloves.status")));
							l.Item().Element(x => LabelCheck(x, "Neoprene Gloves", ppe.Bool("neopreneGloves.status")));
							l.Item().Element(x => LabelCheck(x, "Safety Harness", ppe.Bool("safetyHarness.status")));
						});
						r.RelativeItem().PaddingLeft(Px(8)).Column(l =>
						{
							l.Item().Element(x => LabelCheck(x, "Fall Protection", ppe.Bool("fallProtection.status")));
							l.Item().Element(x => LabelCheck(x, "Safety Shoes", ppe.Bool("safetyShoes.status")));
							l.Item().Element(x => LabelCheck(x, "Ear Protection", ppe.Bool("earProtection.status")));
							l.Item().Element(x => LabelCheck(x, "Breathing Protection", ppe.Bool("breathingProtection.status")));
							l.Item().Element(x => LabelCheck(x, "Other", ppe.Bool("otherPpe.status"), ppe.Other("otherPpe.value")));
						});
					});
					c.Item().Element(b_ => Edges(b_, top: true)).Element(x => Pad(x))
						.Element(x => LabelAbove(x, "Any further PPE required to be included in the JHA?", ppe.Text("otherPpeRequiredInJHA")));
				});

				row.RelativeItem().Column(c =>
				{
					c.Item().Element(x => Heading(x, "WORK AREA", top: false));
					c.Item().Element(x => Pad(x)).Row(r =>
					{
						r.RelativeItem().PaddingRight(Px(8)).Column(l =>
						{
							l.Item().Element(x => LabelCheck(x, "Demarcation", area.Bool("demarcation.status")));
							l.Item().Element(x => LabelCheck(x, "Fire Extinguisher", area.Bool("fireExtinguisher.status")));
						});
						r.RelativeItem().PaddingRight(Px(8)).Column(l =>
						{
							l.Item().Element(x => LabelCheck(x, "Warning Signs", area.Bool("warningSigns.status")));
							l.Item().Element(x => LabelCheck(x, "Other", area.Bool("otherPpeWorkArea.status"), area.Other("otherPpeWorkArea.value")));
						});
					});
				});
			});

			// Precautions
			col.Item().Element(b_ => Edges(b_, left: true, right: true, bottom: true)).Column(box =>
			{
				box.Item().Element(c => Heading(c, "PRECAUTIONS", top: false));
				box.Item().Element(c => Pad(c)).Element(c => LabelAbove(c,
					"Precautions for the work (in addition to those overleaf, and stated in associated certificates):", pre.Text("precautionsForWork")));
				box.Item().Element(b_ => Edges(b_, top: true)).Element(c => Pad(c)).Element(c => LabelAbove(c,
					"Additional precautions for Lead Permit Issuer (LPI) and Engineer:", pre.Text("additionalPrecautions")));
				box.Item().Element(b_ => Edges(b_, top: true)).Element(c => Pad(c)).PaddingBottom(Px(8)).Row(r =>
				{
					r.AutoItem().AlignMiddle().Text("Reference made to attached Method of Statement?");
					r.ConstantItem(Px(16));
					r.AutoItem().AlignMiddle().Element(x => Check(x, pre.Bool("methodOfStatementAttached")));
				});
			});
		});
	}

	private static void MeasureTitle(IContainer c, string title) =>
		c.Element(b_ => Edges(b_, bottom: true)).Padding(Px(4)).PaddingLeft(Px(4)).Text(title).Bold();

	private static void MeasureField(IContainer c, string label, string value) =>
		c.Element(b_ => Edges(b_, bottom: true)).Element(x => Pad(x)).Element(x => LabelAbove(x, label, value));

	private static void LabelNumber(IContainer c, string label, string value) =>
		c.Row(row =>
		{
			row.RelativeItem().Text(label);
			row.AutoItem().PaddingRight(Px(4)).Text(value).FontColor(ValueColor);
		});

	// ── Main form: page 3 ───────────────────────────────────────────────────

	private void MainFormPage3(IContainer container)
	{
		var pre = _form["precautions"];

		container.Element(b_ => Edges(b_, left: true, right: true, bottom: true)).Column(box =>
		{
			box.Item().Element(c => Heading(c, "DECLARATIONS"));
			box.Item().Element(c => Pad(c)).Column(c =>
			{
				c.Item().Element(x => CheckLabel(x, pre.Bool("declaration"),
					"I certified that I have fully considered the implications of the work in line with the responsibilities details " +
					"in the permit to work procedure and that the above precautions are adequate for the work to be carried out provided " +
					"that the precautions are established and maintained.", alignTop: true));
				c.Item().Element(x => Rule(x, 8));

				c.Item().Row(r =>
				{
					r.AutoItem().AlignMiddle().Text("Engineering Advice Required?");
					r.ConstantItem(Px(8));
					r.AutoItem().AlignMiddle().Element(x => Check(x, pre.Bool("engineeringAdviceRequired")));
				});
				c.Item().Element(x => Rule(x, 8));

				c.Item().PaddingBottom(Px(4)).Text("Declaration by Permit Issuer (PI)");
				c.Item().Element(x => CheckLabel(x, pre.Bool("permitIssuerDeclaration.authorizedForExecution"),
					"Authorised for execution without additional certificates"));
				c.Item().Element(x => Rule(x, 8));

				c.Item().PaddingBottom(Px(4)).Text("Declaration by Lead Permit Issuer (LPI)");
				c.Item().Element(x => CheckLabel(x, pre.Bool("leadPermitIssuerDeclaration.authorizedForExecution"),
					"Authorised for execution only in accordance with attached certificates"));
				c.Item().Element(x => Rule(x, 8));

				c.Item().PaddingBottom(Px(4)).Text("Permit validity (where work is going to last more than the duration of a day)");
				c.Item().Element(x => CheckLabel(x, pre.Bool("permitValidity"),
					"Permit Issuer & Permit Holder confirm by signing that the precautions required to work safely have been " +
					"complied with prior to starting work each day or work period. At the close of each day the Permit Issuer " +
					"& Permit Holder sign to confirm that the area has been left in a safe condition (although it is still " +
					"not an operational area, i.e. it has not been handed back) and that all staff have left the site.", alignTop: true));
			});
		});
	}
}
