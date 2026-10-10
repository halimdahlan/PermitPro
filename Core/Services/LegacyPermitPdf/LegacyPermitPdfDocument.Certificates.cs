#nullable enable

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace PermitPro.Core.Services.LegacyPermitPdf;

// Certificate sections A–H. Field names follow the saved PermitForm JSON (general.certificates[]).
internal sealed partial class LegacyPermitPdfDocument
{
	private const string TableRuleColor = "#dee2e6";   // Bootstrap .table row lines

	private void ComposeCertificate(IContainer container, AttachedCertificate cert)
	{
		switch (cert.Name)
		{
			case "hotwork": HotWork(container, cert); break;
			case "confinedspace": ConfinedSpace(container, cert); break;
			case "radiation": Radiation(container, cert); break;
			case "excavation": Excavation(container, cert); break;
			case "isolation": Isolation(container, cert); break;
			case "methodStatement": MethodStatement(container, cert); break;
			case "liftingHoisting": LiftingHoisting(container, cert); break;
			case "override": Override(container, cert); break;
			default: break;
		}
	}

	/// <summary>Block with 8px sides ("px-2"); <paramref name="bottomPx"/> is its bottom margin.</summary>
	private static IContainer Block2(IContainer c, float bottomPx = 8) => c.PaddingHorizontal(Px(8)).PaddingBottom(Px(bottomPx));

	/// <summary>"YES/NO" (bold) — "Reason" — reason (blue): the "certificate is withdrawn" line.</summary>
	private static void Withdrawn(IContainer c, PermitFormReader d)
	{
		c.Row(r =>
		{
			r.AutoItem().Text(d.YesNo("permitWithdrawn.status")).Bold();
			r.ConstantItem(Px(16));
			r.AutoItem().Text("Reason");
			r.ConstantItem(Px(16));
			r.RelativeItem().Text(d.Text("permitWithdrawn.reason")).FontColor(ValueColor);
		});
	}

	/// <summary>Question and answer, followed by a light rule ("hr" with 5px margins) unless last.</summary>
	private static void QuestionList(IContainer c, params (string Question, string Answer)[] items)
	{
		c.Column(col =>
		{
			for (var i = 0; i < items.Length; i++)
			{
				var item = items[i];
				col.Item().Element(x => LabelAbove(x, item.Question, item.Answer));
				if (i < items.Length - 1)
					col.Item().Element(x => Rule(x, 5));
			}
		});
	}

	// ── C: Radiation ────────────────────────────────────────────────────────

	private void Radiation(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;

		CertificateFrame(container, cert, "RADIATION", box =>
		{
			HeadingGap(box, "GENERAL INFORMATION", top: false);
			box.Item().Element(c => Block2(c)).Text("Only valid for routine activities ionise radiation source. (Only to be completed by authorised persons).");

			HeadingGap(box, "TO BE COMPLETED BY THE PERMIT HOLDER");
			box.Item().Element(c => Block2(c, 16)).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Element(x => LabelValue(x, "Radioactive material being used, please specify: ", d.Text("radioactiveMaterialUsed")));
				c.Item().PaddingBottom(Px(16)).Text(t =>
				{
					t.Span("Activity of material: ");
					t.Span(d.Text("materialActivity")).FontColor(ValueColor);
					t.Span(" Bq/mCi");
				});
				c.Item().PaddingBottom(Px(16)).Element(x => LabelValue(x, "Contained within: ", d.Text("containedWithin")));

				c.Item().PaddingBottom(Px(16)).Column(x =>
				{
					x.Item().PaddingBottom(Px(16)).Element(y => CheckLabel(y, d.Bool("xray.state"), "X-ray machine"));
					void Spec(string label, string value, string? unit, bool last = false) =>
						x.Item().PaddingLeft(Px(24)).PaddingBottom(last ? 0 : Px(8)).Row(r =>
						{
							r.ConstantItem(Px(140)).Text(label);
							r.ConstantItem(Px(16));
							r.RelativeItem().Text(t =>
							{
								t.Span(value).FontColor(ValueColor);
								if (unit != null) t.Span("    " + unit);
							});
						});
					Spec("Manufacturer/model", d.Text("xray.manufacturer"), null);
					Spec("Max tube voltage", d.Text("xray.maxTubeVoltage"), "kV");
					Spec("Max tube current", d.Text("xray.maxTubeCurrent"), "mA", last: true);
				});

				c.Item().PaddingBottom(Px(16)).Element(x => CheckLabel(x, d.Bool("other.state"), "Other equipment, specify:", d.Other("other.value")));
				c.Item().PaddingBottom(Px(16)).Element(x => LabelAbove(x, "Additional information about operation of equipment:", d.Text("additionalInfo")));
				c.Item().PaddingBottom(Px(16)).Row(r =>
				{
					r.AutoItem().Text("Radiation equipment will be used between");
					r.ConstantItem(Px(48));
					r.AutoItem().Element(x => FromTo(x, d.Time("radiationEquipmentUsed.from"), d.Time("radiationEquipmentUsed.to")));
				});
				c.Item().PaddingBottom(Px(16)).Element(x => LabelAbove(x, "Measures to prevent and mitigate exposure:", d.Text("measuresPreventExposure")));
				c.Item().PaddingBottom(Px(24)).Element(x => LabelAbove(x, "Additional controls required:", d.Text("additionalControls")));
				c.Item().AlignCenter().Text("ALL IRREGULARITIES DURING WORK MUST BE REPORTED IMMEDIATELY TO THE PERMIT HOLDER").Bold();
			});

			HeadingGap(box, "VALIDITY OF THE RADIATION PERMIT");
			box.Item().Element(c => Block2(c)).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Text(
					"I certify that I have reviewed the proposed work and I am satisfied that it is adequately described on the " +
					"Permit to Work documents and that the precautions detailed in this certificate are adequate and clearly defined.");
				c.Item().PaddingBottom(Px(8)).Row(r =>
				{
					r.AutoItem().Text("Radiation Permit is allowed between");
					r.ConstantItem(Px(48) + SpaceWidth);
					r.AutoItem().Element(x => FromTo(x, d.Time("permitValidity.from"), d.Time("permitValidity.to"), gapPx: 16 + 48));
				});
			});

			HeadingGap(box, "RADIATION CERTIFICATE IS WITHDRAWN");
			box.Item().Element(c => Block2(c)).Element(c => Withdrawn(c, d));
		});
	}

	// ── D: Excavation ───────────────────────────────────────────────────────

	private void Excavation(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;

		CertificateFrame(container, cert, "EXCAVATION", box =>
		{
			HeadingGap(box, "GENERAL INFORMATION", top: false);
			box.Item().Element(c => Block2(c)).Text(t =>
			{
				t.Span("Estimated excavation depth  ");
				t.Span(d.Text("excavationDepth")).Bold().FontColor(ValueColor);
			});

			HeadingGap(box, "EXCAVATION INSTRUCTIONS APPLICABLE TO THE PERMIT HOLDER WHO IS RESPONSIBLE FOR ASSURING COMPLIANCE");
			box.Item().Element(c => Block2(c)).PaddingRight(Px(24)).Element(c => Bullets(c,
				"When sloping is utilized as a protective measure for excavations up to 6 meters deep, an acceptable sloping is 1.5 horizontal to " +
				"1 vertical. Steeper slopes may be allowed where pre defined Country specific tables or charts exist based on soil classification " +
				"or where recognized soil engineering expertise personnel are involved in defining acceptable steeper slopes.",
				"Identify Overhead electrical lines (or other overhead infrastructure) and assure provisions are in place to prevent contact (or " +
				"encroachment beyond established safe distances when high voltage power lines exist) by equipment.",
				"Underground cabling and piping must be clearly marked /identified prior to excavation.",
				"Within 0.5 meters (1.6 feet) around piping and cabling, only careful hand probing and digging may be used.",
				"Exposed underground piping and cabling to be supported.",
				"Soil removed from excavation must be a minimum of 0.75 meters setback from the edge of the excavation.",
				"Excavations 6 meters or deeper require the services of a specialized Subject Matter Expert (Engineered credentialed or " +
				"similarly recognized soils and protective systems expert) on soil stability and controls who is commissioned to develop a safe excavation plan.",
				"Contaminated soil requires the use of PPE as well as specific Hazardous waste handling/disposal plans.",
				"Utilize confined space entry certificate when work meets the confined space definition.",
				"Barriers and signage must be present to prevent unauthorized entry, and warning systems in place for vehicles.",
				"A designate authorized person(s) must be identified below for daily inspections (may be Permit Holder)."));

			HeadingGap(box, "CONTROLS USED");
			box.Item().Element(c => Block2(c)).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Row(r =>
				{
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("controlsUsed.sloping"), "Sloping", inline: true));
					r.ConstantItem(Px(32));
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("controlsUsed.shoring"), "Shoring", inline: true));
					r.ConstantItem(Px(32));
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("controlsUsed.shielding"), "Shielding", inline: true));
				});
				c.Item().Text("Document below additional controls not otherwise included in this PTW to address job specific threats including:");
				c.Item().PaddingBottom(Px(16)).Element(x => Numbered(x,
					"measures to minimize weather impacts on cave-ins",
					"provisions for safe access and exit for personnel who enter the excavation",
					"additional rescue and emergency response provisions",
					"other threats specific to the job"));
				c.Item().PaddingBottom(Px(16)).Element(x => LabelAbove(x, "Designated authorized person(s) for excavation:", d.Text("designatedAuthorizedPerson")));
				c.Item().PaddingBottom(Px(16)).Element(x => LabelAbove(x, "Additional necessary info not otherwise included elsewhere:", d.Text("additionalInfo")));
				c.Item().Element(x => LabelAbove(x, "Additional space to communicate via sketch information about hazards/threats and controls:", d.Text("additionalSpaceSketch")));
			});

			HeadingGap(box, "EMERGENCY CONTACT INFO");
			box.Item().Element(c => Block2(c)).Row(r =>
			{
				r.AutoItem().Text("Name");
				r.ConstantItem(Px(16));
				r.RelativeItem().Text(d.Text("emergencyContact")).FontColor(ValueColor);
			});

			HeadingGap(box, "VALIDITY OF THE EXCAVATION PERMIT");
			box.Item().Element(c => Block2(c)).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Text(
					"I certify that I have reviewed the proposed work and I am satisfied that the workers understand the hazards and that " +
					"preventive measures are in place with adequate supervision to proceed.");
				c.Item().PaddingBottom(Px(8)).Row(r =>
				{
					r.AutoItem().Text("Excavation Permit is allowed between");
					r.ConstantItem(Px(16) + SpaceWidth);
					r.AutoItem().Element(x => FromTo(x, d.Time("permitValidity.from"), d.Time("permitValidity.to")));
				});
			});

			HeadingGap(box, "EXCAVATION CERTIFICATE IS WITHDRAWN");
			box.Item().Element(c => Block2(c)).Element(c => Withdrawn(c, d));
		});
	}

	// ── E: Isolation (lock out / tag out) ───────────────────────────────────

	private void Isolation(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;

		container.Column(page =>
		{
			page.Item().Element(c => CertificateFrame(c, cert, "SAFE ISOLATION LOCK OUT TAG OUT", box =>
			{
				HeadingGap(box, "IDENTIFICATION OF EQUIPMENT BEING WORKED ON AND THE EQUIPMENT THAT IS LOCKED/TAGGED", top: false);
				box.Item().Element(c => Block2(c)).Column(c =>
				{
					c.Item().PaddingBottom(Px(16)).Text("(specific valves, pumps, motors, switch gears, etc.)");
					c.Item().Element(x => LabelAbove(x,
						"Equipment Being worked on and verified to be at a zero energy state prior to work starting (describe or sketch to make clear):",
						d.Text("equipmentUsed")));
				});

				HeadingGap(box, "TYPE OF LOCK OUT / TAG OUT (LO/TO) USED");
				box.Item().Element(c => Block2(c)).Row(r =>
				{
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("lockOutTagOutType.individual"), "Each authorized individual will apply LO/TO to each isolation device", inline: true));
					r.ConstantItem(Px(48));
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("lockOutTagOutType.group"), "Use of Group LO/TO Using Lock Box", inline: true));
				});

				HeadingGap(box, "ENERGY TYPE ISOLATED");
				box.Item().Element(c => Block2(c)).Column(c =>
				{
					c.Item().PaddingBottom(Px(16)).Text("Mark below (√) the type of energy to be isolated:");
					c.Item().Row(r =>
					{
						foreach (var (key, label) in new[] { ("electrical", "Electrical"), ("steam", "Steam"), ("hydraulic", "Hydraulic"), ("mechanical", "Mechanical"), ("pneumatic", "Pneumatic") })
						{
							r.AutoItem().Element(x => CheckLabel(x, d.Bool($"isolatedEnergyType.{key}"), label, inline: true));
							r.ConstantItem(Px(24));
						}
						r.RelativeItem().Element(x => CheckLabel(x, d.Bool("isolatedEnergyType.other.state"), "Other", d.Raw("isolatedEnergyType.other.value")?.Trim()));
					});
				});

				HeadingGap(box, "METHOD OF ISOLATION");
				box.Item().Element(c => Block2(c)).Column(c =>
				{
					c.Item().PaddingBottom(Px(16)).Text("Mark below (√) isolation being used:");
					c.Item().Row(r =>
					{
						foreach (var (key, label) in new[] { ("lockTag", "Lock & Tag"), ("tagOnly", "Tag Only"), ("blinds", "Blinds"), ("doubleBlockBleed", "Double Block & Bleed") })
						{
							r.AutoItem().Element(x => CheckLabel(x, d.Bool($"isolationMethod.{key}"), label, inline: true));
							r.ConstantItem(Px(24));
						}
						r.RelativeItem().Element(x => CheckLabel(x, d.Bool("isolationMethod.other.state"), "Other", d.Raw("isolationMethod.other.value")?.Trim() ?? PermitFormReader.Dash));
					});
					c.Item().PaddingTop(Px(16)).Row(r =>
					{
						// Fixed width (as printed before): an auto-sized cell can't hold the list's relative-width lines.
						r.ConstantItem(190f).Border(BorderWidth).BorderColor("#d6d8db").Background(HeadingFill).Padding(Px(16)).Column(card =>
						{
							card.Item().PaddingBottom(Px(8)).Text("Sequence of Lockout").Bold().FontSize(10.9f);
							card.Item().Element(x => Numbered(x,
								"Prepare for shutdown", "Equipment shut down", "Identify all equipment isolation points",
								"Apply L/O-T/O device(s)", "Release stored energy", "Verify equipment will not start", "Perform work",
								"Clear tools, inspect equipment", "Clear personnel from area", "Remove lock out/tag out devices", "Restart equipment"));
						});
						r.RelativeItem();
					});
				});

				HeadingGap(box, "AUTHORIZED INDIVIDUALS");
				box.Item().Element(c => Block2(c)).Column(c =>
				{
					c.Item().PaddingBottom(Px(16)).Text("Individuals doing the Lock Out/Tag Out including verification of zero energy state:");
					c.Item().Element(x => ValueTable(x, new[] { 120f, 300f, 300f },
						new[] { "Date", "Name", "Company" },
						d.Items("authorizedIndividuals").Select(i => new[] { i.Date("date"), i.Raw("name"), i.Raw("company") }),
						striped: true));
				});

				HeadingGap(box, "VALIDITY OF THE SAFE ISOLATION LOCK OUT TAG OUT PERMIT");
				box.Item().Element(c => Block2(c)).Column(c =>
				{
					c.Item().PaddingBottom(Px(16)).Text(
						"I certify that I have reviewed the proposed work and I am satisfied that the workers understand the hazards " +
						"and that preventive measures are in place with adequate supervision to proceed.");
					c.Item().PaddingBottom(Px(8)).Row(r =>
					{
						r.AutoItem().Text("The isolation work is allowed between");
						r.ConstantItem(Px(16) + SpaceWidth);
						r.AutoItem().Element(x => FromTo(x, d.Time("permitValidity.from"), d.Time("permitValidity.to"), gapPx: 8));
					});
				});

				HeadingGap(box, "RECONNECTION");
				box.Item().Element(c => Block2(c)).Row(r =>
				{
					// Always ticked on the form.
					r.ConstantItem(CheckSize).Element(x => Check(x, true));
					r.ConstantItem(Px(16));
					r.RelativeItem().Text("I have verified that the equipment under Lockout/Tagout is no longer locked or tagged, has been re-connected and is in safe working condition.");
				});
			}));

			page.Item().PageBreak();

			page.Item().Element(c => Edges(c, left: true, right: true, bottom: true)).Column(box =>
			{
				HeadingGap(box, "ISOLATION POINT IDENTIFICATION");
				box.Item().Element(c => Block2(c)).Element(c => IsolationPoints(c, d));
			});
		});
	}

	private void IsolationPoints(IContainer container, PermitFormReader d)
	{
		string[] header = { "Isolation Point Identification", "Tag Installed?", "Lock Installed?", "Blind Installed?", "Normal Operating Position", "Date Installed", "Date Removed" };
		float[] widths = { 100.9f, 60f, 63.1f, 64.8f, 92.8f, 56.8f, 61.6f };

		container.PaddingBottom(Px(16)).Table(table =>
		{
			table.ColumnsDefinition(cols => { foreach (var w in widths) cols.RelativeColumn(w); });
			table.Header(head =>
			{
				foreach (var h in header)
					head.Cell().BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth).Padding(Px(8))
						.Text(h).Bold().FontColor(QuestPDF.Helpers.Colors.Black);
			});

			var index = 0;
			foreach (var p in d.Items("isolationPoints"))
			{
				var fill = index++ % 2 == 0 ? StripeFill : null;
				IContainer Cell() { var c = table.Cell().BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth); return (fill != null ? c.Background(fill) : c).Padding(Px(8)); }

				Cell().Text(p.Text("isolationPoint")).FontColor(ValueColor);
				Cell().Element(x => Check(x, p.Bool("tagInstalled")));
				Cell().Element(x => Check(x, p.Bool("lockInstalled")));
				Cell().Element(x => Check(x, p.Bool("blindInstalled")));
				Cell().Text(p.Text("normalOperatePos")).FontColor(ValueColor);
				Cell().Text(p.Date("dateInstalled")).FontColor(ValueColor);
				Cell().Text(p.Date("dateRemoved")).FontColor(ValueColor);
			}
		});
	}

	// ── F: Method statement ─────────────────────────────────────────────────

	private void MethodStatement(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;

		CertificateFrame(container, cert, "METHOD STATEMENT", box =>
		{
			HeadingGap(box, "DESCRIPTION OF WORKS", top: false);
			box.Item().Element(c => Block2(c)).Element(c => QuestionList(c,
				("What the work involve?", d.Text("worksDescription.1")),
				("How is it going to be completed? Summarise the operation:", d.Text("worksDescription.2")),
				("Where is the work going to be completed? (Summary - more details on Section 5)", d.Text("worksDescription.3")),
				("Duration of the work? (Stating any times when access to the Terminal will not be required)", d.Text("worksDescription.4")),
				("Include site location drawings if appropriate?", d.Text("worksDescription.5")),
				("Are there any restrictions e.g. overhead power lines?", d.Text("worksDescription.6")),
				("Are local permits required (Local regulations)?", d.Text("worksDescription.7")),
				("Any other details", d.Text("worksDescription.8"))));

			HeadingGap(box, "NAMES OF RESPONSIBLE PERSONS");
			box.Item().Element(c => Block2(c)).Element(c => QuestionList(c,
				("Who is going to be working on the project?", d.Text("responsiblePersonNames.1")),
				("Who will be in charge?", d.Text("responsiblePersonNames.2")),
				("Who has specific responsibilities? e.g. Plant, personnel, materials, etc.", d.Text("responsiblePersonNames.3")),
				("Who is to make contact with local authorities?", d.Text("responsiblePersonNames.4")),
				("Any other details?", d.Text("responsiblePersonNames.5"))));

			HeadingGap(box, "PLANT AND EQUIPMENT");
			box.Item().Element(c => Block2(c)).Column(c =>
			{
				c.Item().Element(x => LabelAbove(x, "What plant equipment and tools are required to do the job?", d.Text("plantEquipment.1")));
				c.Item().Element(x => Rule(x, 5));
				c.Item().PaddingBottom(Px(8)).Text("Who will be in charge?");
				foreach (var (key, label) in new[] { ("2", "Tools (for cold work)"), ("3", "Tools (that may generate sparks, ignitions sources, etc.)"),
					("4", "Cherry pickers"), ("5", "Scaffolding"), ("6", "Loading of materials"), ("7", "Others, please specify") })
				{
					c.Item().PaddingLeft(Px(16)).PaddingBottom(Px(8)).Element(x => LabelAbove(x, label, d.Text($"plantEquipment.{key}")));
				}
				c.Item().Element(x => Rule(x, 5));
				c.Item().Element(x => QuestionList(x,
					("What certificates, if any, are required for plant or equipment?", d.Text("plantEquipment.8")),
					("What training, if any, is required for operatives?", d.Text("plantEquipment.9"))));
			});

			HeadingGap(box, "COMMUNICATIONS WITH WORKFORCE");
			box.Item().Element(c => Block2(c)).Element(c => QuestionList(c,
				("How are the procedures, hazards to be communicates to the workforce?", d.Text("communicationsWorkforce.1")),
				("Toolbox talks, safety meetings etc.", d.Text("communicationsWorkforce.2")),
				("Include records of any such meetings and record attendance", d.Text("communicationsWorkforce.3")),
				("Others, please specify", d.Text("communicationsWorkforce.4"))));

			HeadingGap(box, "HOW WILL THE WORK BE COMPLETED (PROCEDURE STEP-BY-STEP)");
			box.Item().Element(c => Block2(c)).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Row(r =>
				{
					r.AutoItem().Text("Proposed start date/time:");
					r.ConstantItem(Px(16) + SpaceWidth);
					r.AutoItem().Text(d.DateTime("workToBeCompleted.1")).Bold().FontColor(ValueColor);
					r.ConstantItem(Px(48));
					r.AutoItem().Text("Finish date/time:");
					r.ConstantItem(Px(16) + SpaceWidth);
					r.AutoItem().Text(d.DateTime("workToBeCompleted.2")).Bold().FontColor(ValueColor);
				});
				c.Item().PaddingBottom(Px(8)).Element(x => LabelAbove(x, "Details", d.Text("workToBeCompleted.3")));
			});
		});
	}

	// ── G: Lifting and hoisting ─────────────────────────────────────────────

	private void LiftingHoisting(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;

		string[] checks =
		{
			"Trained/qualified person undertaking this work and screened by Terminal Team",
			"Crane inspected and all related certificate still valid (upload the document)",
			"Lifting plan communicated with Terminal team and ground team",
			"Pre-job discussion conducted",
			"Person in charge and lifting team (signalman, banksman, rigger etc.) identified",
			"Lifting plan communicated with Terminal team and ground team",
			"JHA and JMS communicated with Terminal team and ground team",
			"The working area is defined with barriers/signs",
			"The lift area controlled and is everyone clear if the load falls or swings",
			"Environmental limits (e.g. maximum permissible wind speed) for the lift communicated",
			"Signalling methods and communication agreed and clear",
			"Space constrain addressed and terminal critical equipment identified",
			"Terminal critical equipment and property at the load swinging radius safeguarded (removed, hard barricaded etc.)",
		};

		CertificateFrame(container, cert, "LIFTING & HOISTING", box =>
		{
			HeadingGap(box, "GENERAL", top: false);
			box.Item().Element(c => Block2(c, 0)).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Row(r =>
				{
					r.RelativeItem().PaddingRight(Px(24)).Element(x => LabelAbove(x, "Specify location within terminal:", d.Text("location")));
					r.RelativeItem().PaddingLeft(Px(24)).Column(x =>
					{
						x.Item().Element(y => LabelValue(y, "Lifting Plan No: ", d.Text("liftingPlanNo")));
						x.Item().PaddingTop(Px(4)).Text("(Lifting Plan to be attached for Level 2 lift)");
					});
				});
				c.Item().PaddingBottom(Px(8)).Text("Type of lift (Get advise from HSSE Department)");
				c.Item().PaddingBottom(Px(8)).Row(r =>
				{
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("typeOfLift.level1"), "Level 1 (Simple Lift)", inline: true));
					r.ConstantItem(Px(48));
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("typeOfLift.level2"), "Level 2 (Complicated and Complex Lift)", inline: true));
				});
			});

			HeadingGap(box, "SAFE CHECK FOR SAFE LIFTING");
			box.Item().Element(c => Block2(c, 8)).Column(list =>
			{
				list.Item().PaddingBottom(Px(8)).Row(r =>
				{
					r.RelativeItem().Text(" ");
					foreach (var caption in new[] { "YES", "NO", "N/A" })
						r.ConstantItem(Px(30)).AlignCenter().Text(caption).Bold();
				});
				for (var i = 0; i < checks.Length; i++)
				{
					var item = d[$"safeChecks.{i + 1}"];
					var label = checks[i];
					list.Item().PaddingBottom(Px(8)).Row(r =>
					{
						r.RelativeItem().AlignMiddle().Text(label);
						foreach (var key in new[] { "yes", "no", "na" })
							r.ConstantItem(Px(30)).AlignCenter().AlignMiddle().Element(x => Check(x, item.Bool(key)));
					});
				}
			});

			HeadingGap(box, "VALIDITY OF THE LIFTING AND HOISTING PERMIT");
			box.Item().Element(c => Block2(c, 0)).Column(c =>
			{
				c.Item().PaddingBottom(Px(8)).Text("Lead Permit Issuer (LPI)").Bold();
				c.Item().PaddingBottom(Px(8)).Text("Lifting and Hoisting Work as described in the Permit to Work is allowed between");
				c.Item().Element(x => FromTo(x, d.DateTime("permitValidity.from"), d.DateTime("permitValidity.to")));
				c.Item().Element(x => Rule(x, 16));

				c.Item().PaddingBottom(Px(8)).Text("Signed Engineering (if required)").Bold();
				c.Item().PaddingBottom(Px(8)).Element(x => Signed(x, d.Bool("permitValidity.signedEngineering"),
					"I certify that I have reviewed the proposed work and I am satisfied that it is adequately described on the Permit to Work " +
					"and that the precautions detailed are adequate and clearly defined."));
				c.Item().Element(x => Rule(x, 16));

				c.Item().PaddingBottom(Px(8)).Text("Permit Holder (PH)").Bold();
				c.Item().PaddingBottom(Px(24)).Element(x => Signed(x, d.Bool("permitValidity.signedPH"),
					"I certify that I am aware of the planned Lifting and Hoisting Work and that provided the precautions detailed on the " +
					"Permit to Work are established and maintained the work may progress, subjected to the Permit to Work being validated."));
			});

			HeadingGap(box, "LIFTING AND HOISTING CERTIFICATE IS WITHDRAWN");
			box.Item().Element(c => Block2(c)).Element(c => Withdrawn(c, d));
		});
	}

	/// <summary>Checkbox, 16px gap, statement (signature-style declarations).</summary>
	private void Signed(IContainer c, bool isChecked, string text)
	{
		c.Row(r =>
		{
			r.ConstantItem(CheckSize).PaddingTop(1f).Element(x => Check(x, isChecked));
			r.ConstantItem(Px(16));
			r.RelativeItem().Text(text);
		});
	}

	// ── H: Safeguarding systems override ────────────────────────────────────

	private void Override(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;

		CertificateFrame(container, cert, "SAFEGUARDING SYSTEMS OVERRIDE", box =>
		{
			box.Item().Padding(Px(8)).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Text(
					"Override of safeguarding protective devices may utilize a standing procedure, the MOC process, " +
					"or PTW process (including this certificate).").Bold();
				c.Item().Text(
					"This PTW Override may not be utilized for the purpose of preventing an alarm trip associated with a known actual " +
					"exceedence of an established operating parameter (pressure, flow, temperature, fill height, etc) for which the device is " +
					"intended. It may be utilized for scheduled and planned overrides (typically for testing or maintenance) when the operating " +
					"parameters are otherwise controlled within approved limits during the override period.").Bold();
			});

			HeadingGap(box, "TO BE COMPLETED BY THE PERMIT HOLDER");
			box.Item().Element(c => Block2(c)).Element(c => QuestionList(c,
				("Identify the protective device/system including alarms that are to be overridden:", d.Text("general.1")),
				("Name of person responsible for the override while it in place:", d.Text("general.2")),
				("Name of person who will assure that the protective device or system has been properly returned to correct operating condition upon completion of the override:", d.Text("general.3")),
				("Name of person who will assure that Operating personnel are made aware that the override is completed and the device is ready for normal service:", d.Text("general.4")),
				("The risks of the override must be evaluated and sufficient barriers put in place to prevent unintended consequences related to the override. " +
				 "Communication prior to and after the override is critically important. This PTW and certificate are intended to put in place such " +
				 "communications and any other barriers identified to prevent unintended consequences related to the override during the period of and after " +
				 "its’ implementation. Use the space below to document any additional barriers not already covered elsewhere in this PTW.", d.Text("general.5")),
				("Operating precautions (people and equipment barriers) including measures to prevent a related process upset condition while the override " +
				 "is in place and the plans including communications for return to normal service.", d.Text("general.6"))));

			HeadingGap(box, "PLANNED DURATION OF OVERRIDE");
			box.Item().Element(c => Block2(c)).Column(c =>
			{
				c.Item().PaddingBottom(Px(8)).Text(
					"Over rides extending beyond a single shift must have the PTW for the override re-issued at the beginning of the next shift. " +
					"Longer term overrides (lasting into a weekend) should utilize the MOC process.");
				c.Item().Element(x => FromTo(x, d.DateTime("plannedDuration.from"), d.DateTime("plannedDuration.to")));
			});

			HeadingGap(box, "VALIDITY OF THE OVERRIDE PERMIT");
			box.Item().Element(c => Block2(c)).Column(c =>
			{
				c.Item().PaddingBottom(Px(8)).Text("Lead Permit Issuer (LPI)").Bold();
				c.Item().PaddingBottom(Px(8)).Row(r =>
				{
					r.ConstantItem(CheckSize).PaddingTop(1f).Element(x => Check(x, d.Bool("permitValidity.signedLPI")));
					r.ConstantItem(Px(8));
					r.RelativeItem().Column(x =>
					{
						x.Item().PaddingBottom(Px(8)).Text(
							"I certify that I have reviewed the proposed work and I am satisfied that it is adequately described on the Permit to Work " +
							"documents and that the precautions detailed in this certificate are adequate and clearly defined. The Permit to Work is allowed between:");
						x.Item().Element(y => FromTo(y, d.DateTime("permitValidity.from"), d.DateTime("permitValidity.to")));
					});
				});
				c.Item().Element(x => Rule(x, 10));
				c.Item().PaddingBottom(Px(8)).Text("Permit Holder (PH)").Bold();
				c.Item().Row(r =>
				{
					r.ConstantItem(CheckSize).PaddingTop(1f).Element(x => Check(x, d.Bool("permitValidity.signedPH")));
					r.ConstantItem(Px(8));
					r.RelativeItem().Text(
						"I certify that I am aware of the planned work and that the precautions detailed on the Permit documents are established " +
						"and will be maintained throughout the work activity involving the over ride.");
				});
			});
		});
	}

	/// <summary>Numbered list (Bootstrap &lt;ol&gt;).</summary>
	private static void Numbered(IContainer container, params string[] items)
	{
		// Numbers right-aligned in the list's 32px indent, as in the browser.
		container.Column(col =>
		{
			for (var i = 0; i < items.Length; i++)
			{
				var n = i + 1;
				var item = items[i];
				col.Item().Row(row =>
				{
					row.ConstantItem(Px(32)).AlignRight().PaddingRight(Px(4)).Text($"{n}.");
					row.RelativeItem().Text(item);
				});
			}
		});
	}

	/// <summary>Letter | title | "PTW NO:" row, then the bordered body.</summary>
	private void CertificateFrame(IContainer container, AttachedCertificate cert, string title, Action<ColumnDescriptor> body)
	{
		container.Column(col =>
		{
			col.Item().Element(Bordered).Row(row =>
			{
				row.ConstantItem(54.6f).PaddingLeft(16.4f).AlignMiddle()
					.Text(cert.Letter).Bold().FontSize(22.7f).LineHeight(1.2f);
				row.RelativeItem().Element(b_ => Edges(b_, left: true, right: true))
					.PaddingHorizontal(Px(16)).AlignMiddle().Text(title).Bold().FontSize(10.9f);
				row.ConstantItem(Px(250)).PaddingLeft(Px(16)).PaddingRight(Px(16) + 4.6f).AlignMiddle().AlignRight().Text(t =>
				{
					t.Span("PTW NO: ");
					t.Span(_model.PermitNo).Bold();
				});
			});

			col.Item().Element(b_ => Edges(b_, left: true, right: true, bottom: true)).Column(body);
		});
	}

	/// <summary>Bootstrap ".table": bold header, light row lines.</summary>
	// Roboto has no subscript digits, so "O₂" is drawn as "O" plus a subscript "2".
	private static void Chemical(TextDescriptor text, string value)
	{
		var parts = value.Split('₂');
		for (var i = 0; i < parts.Length; i++)
		{
			if (i > 0) text.Span("2").Subscript();
			if (parts[i].Length > 0) text.Span(parts[i]);
		}
	}

	private static void ReadingsTable(IContainer container, (string Header, float? WidthPx)[] columns, IEnumerable<string[]> rows)
	{
		container.PaddingBottom(Px(16)).Table(table =>
		{
			table.ColumnsDefinition(cols =>
			{
				foreach (var (_, width) in columns)
				{
					if (width.HasValue) cols.ConstantColumn(Px(width.Value));
					else cols.RelativeColumn();
				}
			});

			table.Header(header =>
			{
				for (var i = 0; i < columns.Length; i++)
				{
					header.Cell().BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth).Padding(Px(8))
						.DefaultTextStyle(x => x.Bold()).Text(t => Chemical(t, columns[i].Header));
				}
			});

			foreach (var row in rows)
			{
				foreach (var value in row)
					table.Cell().BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth).Padding(Px(8)).Text(value);
			}
		});
	}

	// ── A: Hot work ─────────────────────────────────────────────────────────

	private void HotWork(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;

		CertificateFrame(container, cert, "HOT WORK", box =>
		{
			box.Item().Element(c => Heading(c, "GENERAL INFORMATION", top: false));
			box.Item().Padding(Px(8)).Element(c => LabelValue(c, "Emergency contact number: ", d.Text("emergencyContact")));

			box.Item().Element(c => Heading(c, "HOT WORK CHECKLIST"));
			box.Item().Padding(Px(8)).Column(list =>
			{
				string[] items =
				{
					"Communication link has been established with Operations control room.",
					"Calibrated gas tester to be used by AGT.",
					"The working area is defined with barriers or signs to prevent un-authorized entry.",
					"Designated fire watch is assigned and understands their role.",
					"Area within 15 meters of ignition sources are free of flammable materials or such materials are covered with fire retardant material.",
					"Sewer and drain systems and other low lying areas are protected against sparks within 15 meters.",
					"Fire fighting materials are present and ready for use.",
				};
				for (var i = 0; i < items.Length; i++)
				{
					var index = i;
					list.Item().Element(x => CheckLabel(x, d.Bool($"workChecklist[{index}].value"), items[index]));
				}
			});

			box.Item().Element(c => Heading(c, "HOT WORK IGNITION SOURCES (MARK THE APPLICABLE SOURCES FOR THE WORK BEING PERFORMED)"));
			box.Item().Padding(Px(8)).Row(row =>
			{
				string[] sources = { "Welding", "Burning", "Gas Cutting", "Grinding" };
				for (var i = 0; i < sources.Length; i++)
				{
					var index = i;
					row.AutoItem().Element(x => CheckLabel(x, d.Bool($"ignitionSources[{index}].value"), sources[index], inline: true));
					row.ConstantItem(Px(48));
				}
				row.RelativeItem().Element(x => CheckLabel(x, d.Bool("ignitionSources[4].value"), "Other", d.Other("ignitionSources[4].other")));
			});

			box.Item().Element(b_ => Edges(b_, top: true)).Padding(Px(8))
				.Element(c => LabelValue(c, "Gas Tests Performed by: ", d.Text("gasTestPerformedBy.name")));

			box.Item().Element(c => Heading(c, "INITIAL LEL READING BY AGT: HOT WORK NOT TO COMMENCE UNLESS LEL READING IS 0% (<% CONSIDERED NEGLIGIBLE AND OK)"));
			box.Item().Row(row =>
			{
				row.RelativeItem().Element(b_ => Edges(b_, right: true)).Padding(Px(8))
					.Element(c => LabelValue(c, "Date and Time: ", d.DateTime("lowExplosionLevel.initialTestDateTime")));
				row.RelativeItem().Padding(Px(8))
					.Element(c => LabelValue(c, "Initial LEL % Reading: ", d.Text("lowExplosionLevel.initialReading")));
			});

			box.Item().Element(c => Heading(c, "LEL TESTING RESULTS DURING THE HOT WORK: WORK TO BE HALTED IF LEL READINGS ARE NOT LESS THAN 1% LEL"));
			box.Item().Padding(Px(8)).Column(c =>
			{
				c.Item().Text("If non continuous testing is done specify testing frequency required and record results below:");
				c.Item().Element(x => LabelValue(x, "Enter required frequency here: ", d.Text("lowExplosionLevel.requiredFrequency")));
				c.Item().PaddingTop(Px(16)).Element(x => ReadingsTable(x,
					new (string, float?)[] { ("Time of reading", 200), ("LEL reading", null) },
					d.Items("lowExplosionLevel.readings").Select(r => new[] { r.Raw("time") ?? "", r.Raw("value") ?? "" })));
			});

			box.Item().Element(c => Heading(c, "OTHER ATMOSPHERIC TESTING RESULTS IF REQUIRED: ENTER ANY POTENTIALLY TOXIC GASES TO BE MONITORED BELOW:"));
			box.Item().Padding(Px(8)).Element(x => ReadingsTable(x,
				new (string, float?)[] { ("Time of reading", 200), ("O₂ reading", null) },
				d.Items("atmosTestReadings").Select(r => new[] { r.Raw("time") ?? "", r.Raw("value") ?? "" })));

			box.Item().Element(c => Heading(c, "VALIDITY OF THE HOT WORK CERTIFICATE"));
			box.Item().Padding(Px(8)).Column(c =>
			{
				// Always ticked on the form.
				c.Item().Element(x => CheckLabel(x, true,
					"I certify that I have reviewed the proposed work and I am satisfied that the workers understand the " +
					"hazards and that preventive measures are in place with adequate supervision to proceed.", alignTop: true));
				c.Item().Element(x => Rule(x, 16, 8));
				c.Item().Row(row =>
				{
					row.RelativeItem().Text("Hot work as described in the Permit to Work is allowed between:");
					row.RelativeItem().Element(x => FromTo(x, d.Time("permitValidity.from"), d.Time("permitValidity.to")));
				});
			});

			box.Item().Element(c => Heading(c, "HOT WORK CERTIFICATE IS WITHDRAWN"));
			box.Item().Element(c => Pad(c)).Row(row =>
			{
				row.AutoItem().Text(d.YesNo("permitWithdrawn.status")).Bold().FontColor(ValueColor);
				row.ConstantItem(Px(16));
				row.AutoItem().Text("Reason:");
				row.ConstantItem(Px(8));
				row.RelativeItem().Text(d.Text("permitWithdrawn.reason")).FontColor(ValueColor);
			});
		});
	}

	// ── B: Confined space ───────────────────────────────────────────────────

	private const string MutedColor = "#6c757d";   // Bootstrap .text-secondary

	/// <summary>Heading followed by an 8px gap (the confined-space form's "mb-2" headings).</summary>
	private static void HeadingGap(ColumnDescriptor box, string title, bool top = true)
	{
		box.Item().Element(c => Heading(c, title, top: top));
		box.Item().Height(Px(8));
	}

	/// <summary>Block with 16px sides and an 8px bottom margin ("px-3 mb-2").</summary>
	private static IContainer Block(IContainer c) => c.PaddingHorizontal(Px(16)).PaddingBottom(Px(8));

	/// <summary>One "list-group-item" row: label (with optional "Other" value) and YES / NO / N/A boxes.</summary>
	private void YesNoNaRow(ColumnDescriptor list, string label, PermitFormReader item, string? otherValue = null, bool indent = false)
	{
		list.Item().BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth)
			.PaddingHorizontal(Px(16)).PaddingVertical(Px(8)).Row(row =>
			{
				row.RelativeItem().AlignMiddle().PaddingLeft(indent ? Px(24) : 0).Text(text =>
				{
					Chemical(text, label);
					if (otherValue != null)
					{
						text.Span("  ");
						text.Span(otherValue).FontColor(ValueColor);
					}
				});
				foreach (var key in new[] { "yes", "no", "na" })
					row.ConstantItem(Px(28)).AlignCenter().AlignMiddle().Element(x => Check(x, item.Bool(key)));
			});
	}

	/// <summary>Grey "list-group-item" header row: title and YES / NO / N/A captions.</summary>
	private static void YesNoNaHeader(ColumnDescriptor list, string title, bool captions = true)
	{
		list.Item().Background(HeadingFill).BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth)
			.PaddingHorizontal(Px(16)).PaddingVertical(Px(8)).Row(row =>
			{
				row.RelativeItem().Text(title).Bold();
				if (!captions)
					return;
				foreach (var caption in new[] { "YES", "NO", "N/A" })
					row.ConstantItem(Px(28)).AlignCenter().Text(caption).Bold();
			});
	}

	/// <summary>Bootstrap "table-sm table-bordered" (fixed reference table).</summary>
	private static void BorderedTable(IContainer container, float[] relativeWidths, string[] header, string[][] rows)
	{
		container.Table(table =>
		{
			table.ColumnsDefinition(cols =>
			{
				foreach (var w in relativeWidths)
					cols.RelativeColumn(w);
			});

			IContainer Cell(IContainer c) => c.Border(BorderWidth).BorderColor(TableRuleColor).Padding(BorderWidth / 2).Padding(Px(4));

			foreach (var h in header)
				table.Cell().Element(Cell).Text(h).Bold().FontColor(QuestPDF.Helpers.Colors.Black);
			foreach (var row in rows)
				foreach (var v in row)
					table.Cell().Element(Cell).Text(v).FontColor(QuestPDF.Helpers.Colors.Black);
		});
	}

	private void ConfinedSpace(IContainer container, AttachedCertificate cert)
	{
		var d = cert.Data;
		var hazards = d["potentialHazards"];
		var ppe = d["ppeEquipment"];
		var rescue = d["rescueEquipment"];

		// Initial toxic readings are saved as [{ id, value }] (older data may hold plain values).
		string Toxic(int i) => d.Raw($"gasTest.others[{i}].value") is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : d.Text($"gasTest.others[{i}]");

		CertificateFrame(container, cert, "CONFINED SPACE", box =>
		{
			HeadingGap(box, "INSTRUCTIONS", top: false);
			box.Item().PaddingHorizontal(Px(8)).PaddingBottom(Px(8)).Element(c => Bullets(c,
				"This certificate along with its base PTW is intended to assure identification and communication of potential " +
				"hazards and threats along with the preventive and mitigating barrier plans to anyone who could impact or is impacted by the work activity.",
				"Prior to ANY WORKER ENTERING a confined space, the Permit Holder will review the potential hazards and controls " +
				"to prevent harm to impacted workers."));

			HeadingGap(box, "ATTENDANT NAME AND DESCRIPTION OF WORK INFORMATION");
			box.Item().Element(Block).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Element(x => LabelAbove(x, "Designated attendant(s) name:", d.Text("attendants")));
				c.Item().Element(x => LabelAbove(x, "Description of the confined space:", d.Text("workDescription")));
			});

			HeadingGap(box, "GAS TEST PERFORMED BY (AGT NAME AND SIGNATURE)");
			box.Item().Element(Block).Column(c =>
			{
				c.Item().PaddingBottom(Px(24)).Text(
					"Allowable atmospheres: Utilize MSDS and Shell Site Industrial Hygienist as needed for support for specifying " +
					"respirator protection and assuring fitness to work when respirator protection used.").FontColor(MutedColor);
				c.Item().PaddingBottom(Px(24)).Element(x => BorderedTable(x, new[] { 105.3f, 155f, 157.2f, 71.5f },
					new[] { " ", "Without Respiratory Protection", "With Respiratory Protection", "Inert Entry" },
					new[]
					{
						new[] { "Oxygen %", "20 to max. 21.5", ">16 to max. 21.5", "<4" },
						new[] { "Toxics", "< ½ OEL", "< IDLH", "Not applicable" },
						new[] { "Flammables % of LEL", "Not detectable (<1)", "<10; For hot work - not detectable", "<10" },
					}));
				c.Item().Row(r =>
				{
					r.AutoItem().Text("AGT name");
					r.ConstantItem(Px(16));
					r.RelativeItem().Text(d.Text("gasTest.performedBy")).FontColor(ValueColor);
				});
			});

			HeadingGap(box, "INITIAL GAS READINGS BY AGT PRIOR TO PERMIT APPROVAL: INPUT APPLICABLE TOXIC BELOW");
			box.Item().Element(Block).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Row(r =>
				{
					(string Label, string Value)[] cells =
					{
						("Date and time", d.DateTime("gasTest.initialTestWhen")),
						("Initial LEL %", d.Text("gasTest.initialLEL")),
						("Oxygen %", d.Text("gasTest.initialO2")),
						("Toxic", Toxic(0)),
						("Toxic", Toxic(1)),
					};
					for (var i = 0; i < cells.Length; i++)
					{
						if (i > 0) r.ConstantItem(Px(32));
						var cell = cells[i];
						r.RelativeItem().AlignMiddle().Element(x => LabelAbove(x, cell.Label, cell.Value));
					}
				});
				c.Item().PaddingBottom(Px(16)).Text(
					"Monitoring while entrants are inside the confined space. Continuous gas monitoring is the preferred method. " +
					"When non continuous testing is used, document the required interval and the gas monitoring results while workers " +
					"are inside the confined space. Input below toxic gas to be monitored as applicable.").FontColor(MutedColor);
				c.Item().PaddingBottom(Px(24)).Row(r =>
				{
					r.AutoItem().Text("Designated required frequency of testing");
					r.ConstantItem(Px(16));
					r.RelativeItem().Text(d.Text("gasTest.requiredFrequency")).FontColor(ValueColor);
				});
				c.Item().PaddingBottom(Px(8)).Element(x => ValueTable(x,
					new[] { 106f, 176f, 92f, 58f, 60f },
					new[] { "Time of test", "Flammable Gas % LEL", "Oxygen %", "Toxic", "Toxic" },
					d.Items("gasTest.readings").Select(r => new[] { r.Raw("time"), r.Raw("gas"), r.Raw("oxygen"), r.Raw("toxic1"), r.Raw("toxic2") })));

				c.Item().Column(list =>
				{
					YesNoNaHeader(list, "POTENTIAL HAZARDS");
					YesNoNaRow(list, "H₂S (Hydrogen Sulphide)", hazards["hydrogenSulphide"]);
					YesNoNaRow(list, "Oxygen Deficiency (e.g. Nitrogen)", hazards["oxygenDeficiency"]);
					YesNoNaRow(list, "Flammable/Explosive Gas", hazards["flammableGas"]);
					YesNoNaRow(list, "Flammable, Liquids/Solids", hazards["flammableSolids"]);
					YesNoNaRow(list, "Electrical", hazards["electrical"]);
					YesNoNaRow(list, "Ionizing Radiation (X-Ray)", hazards["ionizingRadiation"]);
					YesNoNaRow(list, "Below Ground Level (Cave In)", hazards["belowGroundLevel"]);
					YesNoNaRow(list, "Environmental Conditions", hazards["environmentalConditions"]);
					YesNoNaRow(list, "Engulfment by liquids or solids", hazards["engulfmentLiquidsOrSolids"]);
					YesNoNaRow(list, "Temperature", hazards["temperature"]);
					YesNoNaRow(list, "Ignition Sources", hazards["ignitionSources"]);
					YesNoNaRow(list, "Other", hazards["other1"], hazards.Other("other1.value"));
					YesNoNaRow(list, "Other", hazards["other2"], hazards.Other("other2.value"));
					YesNoNaRow(list, "Other", hazards["other3"], hazards.Other("other3.value"));
				});
			});

			HeadingGap(box, "COMMUNICATION REQUIREMENTS");
			box.Item().Element(Block).Column(c =>
			{
				c.Item().PaddingBottom(Px(8)).Text("Check the (√) means of communication to be used");
				c.Item().PaddingBottom(Px(16)).Row(r =>
				{
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("communicationRequirements.radio"), "Radio", inline: true));
					r.ConstantItem(Px(48));
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("communicationRequirements.voice"), "Voice", inline: true));
					r.ConstantItem(Px(48));
					r.AutoItem().Element(x => CheckLabel(x, d.Bool("communicationRequirements.visual"), "Visual", inline: true));
					r.ConstantItem(Px(48));
					r.RelativeItem().Element(x => CheckLabel(x, d.Bool("communicationRequirements.other.state"), "Other",
						d.Other("communicationRequirements.other.value")));
				});
				c.Item().Element(x => Bullets(x,
					"The Attendant must be able to communicate with the worker(s) inside the confined space at all times.",
					"Should the Attendant be unable to establish communications after several attempts, activate the Rescue Plan"));
			});

			HeadingGap(box, "PERSONAL PROTECTIVE AND EMERGENCY/RESCUE EQUIPMENT REQUIRED ON SITE");
			box.Item().Element(Block).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Text(
					"Check items below in addition to the PPE specified on base PWT. Respiratory equipment requires fitness to work acceptability.");
				c.Item().Column(list =>
				{
					YesNoNaHeader(list, "PPE EQUIPMENT");
					YesNoNaRow(list, "Self Contained Breathing Apparatus", ppe["selfContainedBreathingApparatus"]);
					YesNoNaRow(list, "Supplied Air Breathing Apparatus", ppe["suppliedAirBreathingApparatus"]);
					YesNoNaRow(list, "Ground Fault interrupters in place for portable lighting and small electrical tools", ppe["groundFaultInterrupters"]);
					YesNoNaRow(list, "Ropes (Life Lines)", ppe["ropes"]);
					YesNoNaRow(list, "Full Body Harness", ppe["fullBodyHarness"]);
					YesNoNaRow(list, "Head, Hearing, Hand, Foot, And Body Protection Provided", ppe["protectionProvided"]);
					YesNoNaRow(list, "Barriers in place to prevent unauthorized access/entry to confined space", ppe["barriers"]);
					YesNoNaRow(list, "Ventilation equipment", ppe["ventilationEquipment"]);
					YesNoNaRow(list, "Calibrated Gas testing equipment", ppe["calibratedGasTestingEquipment"]);
					YesNoNaRow(list, "Portable Lighting", ppe["portableLighting"]);
					YesNoNaRow(list, "Other", ppe["other"], ppe.Other("other.value"));

					YesNoNaHeader(list, "RESCUE EQUIPMENT", captions: false);
					YesNoNaRow(list, "Confined Space Rescue Package", rescue["rescuePackage"]);
					YesNoNaRow(list, "Horizontal", rescue["rescuePackage.horizontal"], indent: true);
					YesNoNaRow(list, "Vertical", rescue["rescuePackage.vertical"], indent: true);
					YesNoNaRow(list, "Portable Breathing Air Systems", rescue["portableBreathingAirSystems"]);
					YesNoNaRow(list, "Safe exit (ladders or other means in place)", rescue["safeExit"]);
					YesNoNaRow(list, "Defibrillator available?", rescue["defibrillatorAvailable"]);
					YesNoNaRow(list, "Fire Extinguishers available?", rescue["fireExtinguishersAvailable"]);
					YesNoNaRow(list, "Other", rescue["other"], rescue.Other("other.value"));
				});
			});

			HeadingGap(box, "VALIDITY OF THE CONFINED SPACE PERMIT");
			box.Item().Element(Block).Column(c =>
			{
				c.Item().PaddingBottom(Px(16)).Text(
					"I certify that I have reviewed the proposed work and I am satisfied that the workers understand the hazards " +
					"and that preventive measures are in place with adequate supervision to proceed.");
				// Nested "mb-3" margins collapse into one 16px gap (with the block's own 8px).
				c.Item().PaddingBottom(Px(8)).Row(r =>
				{
					r.AutoItem().Text("Confined Space Permit is allowed between:");
					r.ConstantItem(Px(16));
					r.AutoItem().Element(x => FromTo(x, d.Time("permitValidity.from"), d.Time("permitValidity.to")));
				});
			});

			HeadingGap(box, "CONFINED SPACE CERTIFICATE IS WITHDRAWN");
			box.Item().Element(Block).Row(r =>
			{
				r.AutoItem().Text(d.YesNo("permitWithdrawn.status")).Bold();
				r.ConstantItem(Px(16));
				r.AutoItem().Text("Reason");
				r.ConstantItem(Px(16));
				r.RelativeItem().Text(d.Text("permitWithdrawn.reason")).FontColor(ValueColor);
			});

			HeadingGap(box, "CONFINED SPACE ENTRY RECORD");
			box.Item().Element(Block).Element(x => ValueTable(x,
				new[] { 90.4f, 112f, 112f, 91.2f, 83.3f },
				new[] { "Worker Name", "Worker Signature", "Reason For Entry", "Time Of Entry", "Time Of Exit" },
				d.Items("entryRecords").Select(r => new[] { r.Raw("workerName"), r.Raw("workerSignature"), r.Raw("reason"), r.Raw("timeEntry"), r.Raw("timeExit") })));
		});
	}

	/// <summary>Bootstrap ".table" with relative column widths and blue values.</summary>
	private const string StripeFill = "#f2f2f2";   // Bootstrap .table-striped odd rows

	private static void ValueTable(IContainer container, float[] relativeWidths, string[] header, IEnumerable<string?[]> rows, bool striped = false)
	{
		container.PaddingBottom(Px(16)).Table(table =>
		{
			table.ColumnsDefinition(cols =>
			{
				foreach (var w in relativeWidths)
					cols.RelativeColumn(w);
			});

			table.Header(head =>
			{
				foreach (var h in header)
					head.Cell().BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth).Padding(Px(8))
						.Text(h).Bold().FontColor(QuestPDF.Helpers.Colors.Black);
			});

			var index = 0;
			foreach (var row in rows)
			{
				var fill = striped && index++ % 2 == 0 ? StripeFill : null;
				foreach (var v in row)
				{
					var cell = table.Cell().BorderBottom(BorderWidth).BorderColor(TableRuleColor).PaddingBottom(BorderWidth);
					(fill != null ? cell.Background(fill) : cell).Padding(Px(8))
						.Text(string.IsNullOrWhiteSpace(v) ? " " : v.Trim()).FontColor(ValueColor);
				}
			}
		});
	}

	/// <summary>Bulleted list (Bootstrap &lt;ul&gt;).</summary>
	private static void Bullets(IContainer container, params string[] items)
	{
		container.PaddingLeft(Px(32) - Px(12)).Column(col =>
		{
			foreach (var item in items)
			{
				col.Item().Row(row =>
				{
					row.ConstantItem(Px(12)).Text("•");
					row.RelativeItem().Text(item);
				});
			}
		});
	}

	/// <summary>"From: x   To: y" with bold blue values.</summary>
	private static void FromTo(IContainer c, string from, string to, float gapPx = 16)
	{
		c.Row(row =>
		{
			row.AutoItem().Text(t => { t.Span("From: "); t.Span(from).Bold().FontColor(ValueColor); });
			row.ConstantItem(Px(gapPx) + SpaceWidth);
			row.AutoItem().Text(t => { t.Span("To: "); t.Span(to).Bold().FontColor(ValueColor); });
		});
	}
}
