#nullable disable

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using PermitPro.Core.Data;
using PermitPro.Core.Helpers;
using PermitPro.Core.Interfaces;

using PuppeteerSharp;
using PuppeteerSharp.Media;

using Scriban;

using System.Text;

namespace PermitPro.Core.Services;

public class PermitPdfService : IPermitPdfService
{
	private readonly ApplicationDbContext _dbContext;
	private readonly PTWSettings _ptwSettings;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly ICurrentUserService _currentUserService;
	private readonly IAppSettingsService _appSettings;
	private readonly ILogger<PermitPdfService> _logger;

	public PermitPdfService(
		ApplicationDbContext dbContext
		, PTWSettings ptwSettings
		, IWebHostEnvironment webHostEnvironment
		, ICurrentUserService currentUserService
		, IAppSettingsService appSettingsService
		, ILogger<PermitPdfService> logger)
	{
		_dbContext = dbContext;
		_ptwSettings = ptwSettings;
		_webHostEnvironment = webHostEnvironment;
		_currentUserService = currentUserService;
		_appSettings = appSettingsService;
		_logger = logger;
	}


	public async Task<byte[]> GetPdfBytesAsync(IFormCollection form)
	{
		byte[] pdfResult = null;

		var permit = _dbContext.Permits
			.Include(e => e.Company)
			.Include(e => e.WorkflowHistories)
			.Include(e => e.PermitWorkflowStep)
			.ThenInclude(e => e.Approvers)
			.Where(e => e.Id == Guid.Parse(form["Id"]))
			.FirstOrDefault();

		if (permit == null)
		{
			_logger.LogWarning("PDF export requested for non-existent permit {PermitId}", form["Id"]);
			return null;
		}

		var companyId = permit.Company.Id;

		var jsonObject = JObject.Parse(permit.PermitForm!);
		var jo = JsonConvert.DeserializeObject(permit.PermitForm);

		var mainFormFile = Path.Combine(_webHostEnvironment.WebRootPath, "templates", "html", "permit-main.html");
		var html = File.ReadAllText(mainFormFile, Encoding.UTF8);

		List<dynamic> mainFormCerts = new();
		List<dynamic> attachedCerts = new();

		JArray certificates = (JArray)jsonObject["general"]["certificates"];
		JArray opInfoActivities = (JArray)jsonObject["operation"]["activities"];
		JArray opInfoLocations = (JArray)jsonObject["operation"]["activityLocations"];
		JArray opInfoTools = (JArray)jsonObject["operation"]["activityTools"];
		JArray opInfoProducts = (JArray)jsonObject["operation"]["activityProducts"];

		dynamic formA = null;
		dynamic formB = null;
		dynamic formC = null;
		dynamic formD = null;
		dynamic formE = null;
		dynamic formF = null;
		dynamic formG = null;
		dynamic formH = null;

		foreach (var item in _ptwSettings.Certificates)
		{
			var cert = certificates.SelectToken($"$.[?(@.name=='{item.Name}')]");

			attachedCerts.Add(new
			{
				item.Name,
				item.Title,
				item.Description,
				Data = cert,
			});

			mainFormCerts.Add(new
			{
				item.Name,
				item.Title,
				item.Description,
				Yes = GetCheckBoxImage(cert != null),
				No = GetCheckBoxImage(cert == null),
			});

			if (cert != null)
			{
				html = html.Replace($"__FORM_{item.Description}__", GetCertificateHtmlTemplate(_webHostEnvironment.WebRootPath, item.Name));

				switch (item.Name)
				{
					case "hotwork":
						formA = new
						{
							EmergencyContact = TemplateConvertString(cert["emergencyContact"]),
							WorkChecklist = new
							{
								List1 = GetCheckBoxImage(IsChecked(cert["workChecklist"]?[0]?["value"])),
								List2 = GetCheckBoxImage(IsChecked(cert["workChecklist"]?[1]?["value"])),
								List3 = GetCheckBoxImage(IsChecked(cert["workChecklist"]?[2]?["value"])),
								List4 = GetCheckBoxImage(IsChecked(cert["workChecklist"]?[3]?["value"])),
								List5 = GetCheckBoxImage(IsChecked(cert["workChecklist"]?[4]?["value"])),
								List6 = GetCheckBoxImage(IsChecked(cert["workChecklist"]?[5]?["value"])),
								List7 = GetCheckBoxImage(IsChecked(cert["workChecklist"]?[6]?["value"])),
							},
							IgnitionSources = new
							{
								Source1 = GetCheckBoxImage(IsChecked(cert["ignitionSources"]?[0]?["value"])),
								Source2 = GetCheckBoxImage(IsChecked(cert["ignitionSources"]?[1]?["value"])),
								Source3 = GetCheckBoxImage(IsChecked(cert["ignitionSources"]?[2]?["value"])),
								Source4 = GetCheckBoxImage(IsChecked(cert["ignitionSources"]?[3]?["value"])),
								Source5 = new
								{
									Checkbox = GetCheckBoxImage(IsChecked(cert["ignitionSources"]?[4]?["value"])),
									Value = TemplateConvertString(cert["ignitionSources"]?[4]?["other"], false, true),
								},
							},
							GasTestPerformedBy = new
							{
								Id = cert["gasTestPerformedBy"]?["id"],
								Name = TemplateConvertString(cert["gasTestPerformedBy"]?["name"]),
							},
							LowExplosionLevel = new
							{
								InitialTestDateTime = TemplateConvertString(cert["lowExplosionLevel"]?["initialTestDateTime"]),
								InitialReading = TemplateConvertString(cert["lowExplosionLevel"]?["initialReading"]),
								RequiredFrequency = TemplateConvertString(cert["lowExplosionLevel"]?["requiredFrequency"]),
								Readings = cert["lowExplosionLevel"]?["readings"],
							},
							AtmosTestReadings = cert["atmosTestReadings"],
							PermitValidity = new
							{
								From = TemplateConvertString(cert["permitValidity"]?["from"]),
								To = TemplateConvertString(cert["permitValidity"]?["to"]),
							},
							PermitWithdrawn = new
							{
								YesNo = TemplateConvertString(cert["permitWithdrawn"]?["status"], true),
								Reason = TemplateConvertString(cert["permitWithdrawn"]?["reason"]),
							},
						};
						break;

					case "confinedspace":
						formB = new
						{
							Attendants = TemplateConvertString(cert["attendants"]),
							WorkDescription = TemplateConvertString(cert["workDescription"]),
							GasTest = new
							{
								PerformedBy = TemplateConvertString(cert["gasTest"]?["performedBy"]),
								InitialTestWhen = TemplateConvertString(cert["gasTest"]?["initialTestWhen"]),
								InitialLEL = TemplateConvertString(cert["gasTest"]?["initialLEL"]),
								InitialO2 = TemplateConvertString(cert["gasTest"]?["initialO2"]),
								Others = new
								{
									Toxic1 = TemplateConvertString(cert["gasTest"]?["others"]?[0]),
									Toxic2 = TemplateConvertString(cert["gasTest"]?["others"]?[1]),
								},
								RequiredFrequency = TemplateConvertString(cert["gasTest"]?["requiredFrequency"]),
								Readings = cert["gasTest"]?["readings"],
							},
							PotentialHazards = new
							{
								HydrogenSulphide = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["hydrogenSulphide"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["hydrogenSulphide"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["hydrogenSulphide"]?["na"])),
								},
								OxygenDeficiency = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["oxygenDeficiency"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["oxygenDeficiency"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["oxygenDeficiency"]?["na"])),
								},
								FlammableGas = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["flammableGas"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["flammableGas"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["flammableGas"]?["na"])),
								},
								FlammableSolids = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["flammableSolids"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["flammableSolids"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["flammableSolids"]?["na"])),
								},
								Electrical = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["electrical"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["electrical"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["electrical"]?["na"])),
								},
								IonizingRadiation = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["ionizingRadiation"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["ionizingRadiation"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["ionizingRadiation"]?["na"])),
								},
								BelowGroundLevel = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["belowGroundLevel"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["belowGroundLevel"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["belowGroundLevel"]?["na"])),
								},
								EnvironmentalConditions = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["environmentalConditions"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["environmentalConditions"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["environmentalConditions"]?["na"])),
								},
								EngulfmentLiquidsOrSolids = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["engulfmentLiquidsOrSolids"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["engulfmentLiquidsOrSolids"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["engulfmentLiquidsOrSolids"]?["na"])),
								},
								Temperature = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["temperature"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["temperature"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["temperature"]?["na"])),
								},
								IgnitionSources = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["ignitionSources"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["ignitionSources"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["ignitionSources"]?["na"])),
								},
								Other1 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other1"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other1"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other1"]?["na"])),
									Value = TemplateConvertString(cert["potentialHazards"]?["other1"]?["value"], false, true),
								},
								Other2 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other2"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other2"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other2"]?["na"])),
									Value = TemplateConvertString(cert["potentialHazards"]?["other2"]?["value"], false, true),
								},
								Other3 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other3"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other3"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["potentialHazards"]?["other3"]?["na"])),
									Value = TemplateConvertString(cert["potentialHazards"]?["other3"]?["value"], false, true),
								},
							},
							CommunicationRequirements = new
							{
								Radio = GetCheckBoxImage(IsChecked(cert["communicationRequirements"]?["radio"])),
								Voice = GetCheckBoxImage(IsChecked(cert["communicationRequirements"]?["voice"])),
								Visual = GetCheckBoxImage(IsChecked(cert["communicationRequirements"]?["visual"])),
								Other = new
								{
									Checkbox = GetCheckBoxImage(IsChecked(cert["communicationRequirements"]?["other"]?["state"])),
									Value = TemplateConvertString(cert["communicationRequirements"]?["other"]?["value"], false, true),
								},
							},
							PpeEquipment = new
							{
								SelfContainedBreathingApparatus = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["selfContainedBreathingApparatus"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["selfContainedBreathingApparatus"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["selfContainedBreathingApparatus"]?["na"])),
								},
								SuppliedAirBreathingApparatus = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["suppliedAirBreathingApparatus"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["suppliedAirBreathingApparatus"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["suppliedAirBreathingApparatus"]?["na"])),
								},
								GroundFaultInterrupters = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["groundFaultInterrupters"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["groundFaultInterrupters"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["groundFaultInterrupters"]?["na"])),
								},
								Ropes = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["ropes"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["ropes"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["ropes"]?["na"])),
								},
								FullBodyHarness = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["fullBodyHarness"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["fullBodyHarness"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["fullBodyHarness"]?["na"])),
								},
								ProtectionProvided = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["protectionProvided"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["protectionProvided"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["protectionProvided"]?["na"])),
								},
								Barriers = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["barriers"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["barriers"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["barriers"]?["na"])),
								},
								VentilationEquipment = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["ventilationEquipment"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["ventilationEquipment"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["ventilationEquipment"]?["na"])),
								},
								CalibratedGasTestingEquipment = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["calibratedGasTestingEquipment"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["calibratedGasTestingEquipment"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["calibratedGasTestingEquipment"]?["na"])),
								},
								PortableLighting = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["portableLighting"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["portableLighting"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["portableLighting"]?["na"])),
								},
								Other = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["other"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["other"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["ppeEquipment"]?["other"]?["na"])),
									Value = TemplateConvertString(cert["ppeEquipment"]?["other"]?["value"], false, true),
								},
							},
							RescueEquipment = new
							{
								RescuePackage = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["na"])),
									Horizontal = new
									{
										Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["horizontal"]?["yes"])),
										No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["horizontal"]?["no"])),
										Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["horizontal"]?["na"])),
									},
									Vertical = new
									{
										Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["vertical"]?["yes"])),
										No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["vertical"]?["no"])),
										Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["rescuePackage"]?["vertical"]?["na"])),
									},
								},
								PortableBreathingAirSystems = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["portableBreathingAirSystems"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["portableBreathingAirSystems"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["portableBreathingAirSystems"]?["na"])),
								},
								SafeExit = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["safeExit"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["safeExit"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["safeExit"]?["na"])),
								},
								DefibrillatorAvailable = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["defibrillatorAvailable"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["defibrillatorAvailable"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["defibrillatorAvailable"]?["na"])),
								},
								FireExtinguishersAvailable = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["fireExtinguishersAvailable"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["fireExtinguishersAvailable"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["fireExtinguishersAvailable"]?["na"])),
								},
								Other = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["other"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["other"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["rescueEquipment"]?["other"]?["na"])),
									Value = TemplateConvertString(cert["rescueEquipment"]?["other"]?["value"], false, true),
								},
							},
							PermitValidity = new
							{
								From = TemplateConvertString(cert["permitValidity"]?["from"]),
								To = TemplateConvertString(cert["permitValidity"]?["to"]),
							},
							PermitWithdrawn = new
							{
								YesNo = TemplateConvertString(cert["permitWithdrawn"]?["status"], true),
								Reason = TemplateConvertString(cert["permitWithdrawn"]?["reason"]),
							},
							EntryRecords = cert["entryRecords"],
						};
						break;

					case "radiation":
						formC = new
						{
							RadioactiveMaterialUsed = TemplateConvertString(cert["radioactiveMaterialUsed"]),
							MaterialActivity = TemplateConvertString(cert["materialActivity"]),
							ContainedWithin = TemplateConvertString(cert["containedWithin"]),
							XRay = new
							{
								Checkbox = GetCheckBoxImage(IsChecked(cert["xray"]?["state"])),
								Manufacturer = TemplateConvertString(cert["xray"]?["manufacturer"]),
								MaxTubeVoltage = TemplateConvertString(cert["xray"]?["maxTubeVoltage"]),
								MaxTubeCurrent = TemplateConvertString(cert["xray"]?["maxTubeCurrent"]),
							},
							Other = new
							{
								Checkbox = GetCheckBoxImage(IsChecked(cert["other"]?["state"])),
								Value = TemplateConvertString(cert["other"]?["value"], false, true),
							},
							AdditionalInfo = TemplateConvertString(cert["additionalInfo"]),
							RadiationEquipmentUsed = new
							{
								From = TemplateConvertString(cert["radiationEquipmentUsed"]?["from"]),
								To = TemplateConvertString(cert["radiationEquipmentUsed"]?["to"]),
							},
							MeasuresPreventExposure = TemplateConvertString(cert["measuresPreventExposure"]),
							AdditionalControls = TemplateConvertString(cert["additionalControls"]),
							PermitValidity = new
							{
								From = TemplateConvertString(cert["permitValidity"]?["from"]),
								To = TemplateConvertString(cert["permitValidity"]?["to"]),
							},
							PermitWithdrawn = new
							{
								YesNo = TemplateConvertString(cert["permitWithdrawn"]?["status"], true),
								Reason = TemplateConvertString(cert["permitWithdrawn"]?["reason"]),
							},
						};
						break;

					case "excavation":
						formD = new
						{
							ExcavationDepth = TemplateConvertString(cert["excavationDepth"]),
							ControlsUsed = new
							{
								Sloping = GetCheckBoxImage(IsChecked(cert["controlsUsed"]?["sloping"])),
								Shoring = GetCheckBoxImage(IsChecked(cert["controlsUsed"]?["shoring"])),
								Shielding = GetCheckBoxImage(IsChecked(cert["controlsUsed"]?["shielding"])),
							},
							DesignatedAuthorizedPerson = TemplateConvertString(cert["designatedAuthorizedPerson"]),
							AdditionalInfo = TemplateConvertString(cert["additionalInfo"]),
							AdditionalSpaceSketch = TemplateConvertString(cert["additionalSpaceSketch"]),
							EmergencyContact = TemplateConvertString(cert["emergencyContact"]),
							PermitValidity = new
							{
								From = TemplateConvertString(cert["permitValidity"]?["from"]),
								To = TemplateConvertString(cert["permitValidity"]?["to"]),
							},
							PermitWithdrawn = new
							{
								YesNo = TemplateConvertString(cert["permitWithdrawn"]?["status"], true),
								Reason = TemplateConvertString(cert["permitWithdrawn"]?["reason"]),
							},
						};
						break;

					case "isolation":
						JArray tmp1 = (JArray)cert["authorizedIndividuals"];
						JArray tmp2 = (JArray)cert["isolationPoints"];

						List<dynamic> list1 = new List<dynamic>();
						List<dynamic> list2 = new List<dynamic>();

						if (tmp1 != null)
						{
							foreach (var n in tmp1)
							{
								list1.Add(new
								{
									Date = TemplateConvertString(n["date"].Value<JValue>()),
									Name = TemplateConvertString(n["name"].Value<JValue>()),
									Company = TemplateConvertString(n["company"].Value<JValue>()),
								});
							}
						}

						if (tmp2 != null)
						{
							foreach (var n in tmp2)
							{
								list2.Add(new
								{
									IsolationPoint = TemplateConvertString(n["isolationPoint"].Value<JValue>()),
									TagInstalled = GetCheckBoxImage(IsChecked(n["tagInstalled"].Value<JValue>())),
									LockInstalled = GetCheckBoxImage(IsChecked(n["lockInstalled"].Value<JValue>())),
									BlindInstalled = GetCheckBoxImage(IsChecked(n["blindInstalled"].Value<JValue>())),
									NormalOperatePos = TemplateConvertString(n["normalOperatePos"].Value<JValue>()),
									DateInstalled = TemplateConvertString(n["dateInstalled"].Value<JValue>()),
									DateRemoved = TemplateConvertString(n["dateRemoved"].Value<JValue>()),
								});
							}
						}

						JToken permitValidityFrom = null;
						JToken permitValidityTo = null;

						if (cert["permitValidity"] != null)
						{
							permitValidityFrom = cert["permitValidity"]?["from"];
							permitValidityTo = cert["permitValidity"]?["to"];
						}

						formE = new
						{
							EquipmentUsed = cert["equipmentUsed"],
							LockOutTagOutType = new
							{
								Individual = GetCheckBoxImage(IsChecked(cert["lockOutTagOutType"]?["individual"])),
								Group = GetCheckBoxImage(IsChecked(cert["lockOutTagOutType"]?["group"])),
							},
							IsolatedEnergyType = new
							{
								Electrical = GetCheckBoxImage(IsChecked(cert["isolatedEnergyType"]?["electrical"])),
								Steam = GetCheckBoxImage(IsChecked(cert["isolatedEnergyType"]?["steam"])),
								Hydraulic = GetCheckBoxImage(IsChecked(cert["isolatedEnergyType"]?["hydraulic"])),
								Mechanical = GetCheckBoxImage(IsChecked(cert["isolatedEnergyType"]?["mechanical"])),
								Pneumatic = GetCheckBoxImage(IsChecked(cert["isolatedEnergyType"]?["pneumatic"])),
								Other = new
								{
									Checkbox = GetCheckBoxImage(IsChecked(cert["isolatedEnergyType"]?["other"]?["state"])),
									Value = TemplateConvertString(cert["isolatedEnergyType"]?["other"]?["value"]),
								}
							},
							IsolationMethod = new
							{
								LockTag = GetCheckBoxImage(IsChecked(cert["isolationMethod"]?["lockTag"])),
								TagOnly = GetCheckBoxImage(IsChecked(cert["isolationMethod"]?["tagOnly"])),
								Blinds = GetCheckBoxImage(IsChecked(cert["isolationMethod"]?["blinds"])),
								DoubleBlockBleed = GetCheckBoxImage(IsChecked(cert["isolationMethod"]?["doubleBlockBleed"])),
								Other = new
								{
									Checkbox = GetCheckBoxImage(IsChecked(cert["isolationMethod"]?["other"]?["state"])),
									Value = TemplateConvertString(cert["isolationMethod"]?["other"]?["value"]),
								}
							},
							AuthorizedIndividuals = list1,
							IsolationPoints = list2,
							PermitValidity = new
							{
								From = TemplateConvertString(permitValidityFrom),
								To = TemplateConvertString(permitValidityTo),
							},
						};
						break;

					case "methodStatement":
						formF = new
						{
							WorksDescription = new
							{
								Desc1 = TemplateConvertString(cert["worksDescription"]?["1"]),
								Desc2 = TemplateConvertString(cert["worksDescription"]?["2"]),
								Desc3 = TemplateConvertString(cert["worksDescription"]?["3"]),
								Desc4 = TemplateConvertString(cert["worksDescription"]?["4"]),
								Desc5 = TemplateConvertString(cert["worksDescription"]?["5"]),
								Desc6 = TemplateConvertString(cert["worksDescription"]?["6"]),
								Desc7 = TemplateConvertString(cert["worksDescription"]?["7"]),
								Desc8 = TemplateConvertString(cert["worksDescription"]?["8"]),
							},
							ResponsiblePersonNames = new
							{
								Person1 = TemplateConvertString(cert["responsiblePersonNames"]?["1"]),
								Person2 = TemplateConvertString(cert["responsiblePersonNames"]?["2"]),
								Person3 = TemplateConvertString(cert["responsiblePersonNames"]?["3"]),
								Person4 = TemplateConvertString(cert["responsiblePersonNames"]?["4"]),
								Person5 = TemplateConvertString(cert["responsiblePersonNames"]?["5"]),
							},
							PlantEquipment = new
							{
								Equip1 = TemplateConvertString(cert["plantEquipment"]?["1"]),
								Equip2 = TemplateConvertString(cert["plantEquipment"]?["2"]),
								Equip3 = TemplateConvertString(cert["plantEquipment"]?["3"]),
								Equip4 = TemplateConvertString(cert["plantEquipment"]?["4"]),
								Equip5 = TemplateConvertString(cert["plantEquipment"]?["5"]),
								Equip6 = TemplateConvertString(cert["plantEquipment"]?["6"]),
								Equip7 = TemplateConvertString(cert["plantEquipment"]?["7"]),
								Equip8 = TemplateConvertString(cert["plantEquipment"]?["8"]),
								Equip9 = TemplateConvertString(cert["plantEquipment"]?["9"]),
							},
							CommunicationsWorkforce = new
							{
								Comm1 = TemplateConvertString(cert["communicationsWorkforce"]?["1"]),
								Comm2 = TemplateConvertString(cert["communicationsWorkforce"]?["2"]),
								Comm3 = TemplateConvertString(cert["communicationsWorkforce"]?["3"]),
								Comm4 = TemplateConvertString(cert["communicationsWorkforce"]?["4"]),
							},
							WorkToBeCompleted = new
							{
								Work1 = TemplateConvertString(cert["workToBeCompleted"]?["1"]),
								Work2 = TemplateConvertString(cert["workToBeCompleted"]?["2"]),
								Work3 = TemplateConvertString(cert["workToBeCompleted"]?["3"]),
							},
						};
						break;

					case "liftingHoisting":
						formG = new
						{
							Location = TemplateConvertString(cert["location"]),
							LiftingPlanNo = TemplateConvertString(cert["liftingPlanNo"]),
							TypeOfLift = new
							{
								Level1 = GetCheckBoxImage(IsChecked(cert["typeOfLift"]?["level1"])),
								Level2 = GetCheckBoxImage(IsChecked(cert["typeOfLift"]?["level2"])),
							},
							SafeChecks = new
							{
								Check1 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["1"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["1"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["1"]?["na"])),
								},
								Check2 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["2"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["2"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["2"]?["na"])),
								},
								Check3 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["3"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["3"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["3"]?["na"])),
								},
								Check4 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["4"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["4"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["4"]?["na"])),
								},
								Check5 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["5"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["5"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["5"]?["na"])),
								},
								Check6 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["6"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["6"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["6"]?["na"])),
								},
								Check7 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["7"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["7"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["7"]?["na"])),
								},
								Check8 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["8"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["8"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["8"]?["na"])),
								},
								Check9 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["9"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["9"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["9"]?["na"])),
								},
								Check10 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["10"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["10"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["10"]?["na"])),
								},
								Check11 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["11"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["11"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["11"]?["na"])),
								},
								Check12 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["12"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["12"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["12"]?["na"])),
								},
								Check13 = new
								{
									Yes = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["13"]?["yes"])),
									No = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["13"]?["no"])),
									Na = GetCheckBoxImage(IsChecked(cert["safeChecks"]?["13"]?["na"])),
								},
							},
							PermitValidity = new
							{
								From = TemplateConvertString(cert["permitValidity"]?["from"]),
								To = TemplateConvertString(cert["permitValidity"]?["to"]),
								DateSignedLPI = TemplateConvertString(cert["permitValidity"]?["dateSignedLPI"]),
								SignedEngineering = GetCheckBoxImage(IsChecked(cert["permitValidity"]?["signedEngineering"])),
								DateSignedEngineering = TemplateConvertString(cert["permitValidity"]?["dateSignedEngineering"]),
								SignedPH = GetCheckBoxImage(IsChecked(cert["permitValidity"]?["signedPH"])),
								DateSignedPH = TemplateConvertString(cert["permitValidity"]?["dateSignedPH"]),
							},
							PermitWithdrawn = new
							{
								YesNo = TemplateConvertString(cert["permitWithdrawn"]?["status"], true),
								Reason = TemplateConvertString(cert["permitWithdrawn"]?["reason"]),
							},
						};
						break;

					case "override":
						formH = new
						{
							General = new
							{
								Item1 = TemplateConvertString(cert["general"]?["1"]),
								Item2 = TemplateConvertString(cert["general"]?["2"]),
								Item3 = TemplateConvertString(cert["general"]?["3"]),
								Item4 = TemplateConvertString(cert["general"]?["4"]),
								Item5 = TemplateConvertString(cert["general"]?["5"]),
								Item6 = TemplateConvertString(cert["general"]?["6"]),
							},
							PlannedDuration = new
							{
								From = TemplateConvertString(cert["plannedDuration"]?["from"]),
								To = TemplateConvertString(cert["plannedDuration"]?["to"]),
							},
							PermitValidity = new
							{
								From = TemplateConvertString(cert["permitValidity"]?["from"]),
								To = TemplateConvertString(cert["permitValidity"]?["to"]),
								SignedLPI = GetCheckBoxImage(IsChecked(cert["permitValidity"]?["signedLPI"])),
								DateSignedLPI = TemplateConvertString(cert["permitValidity"]?["dateSignedLPI"]),
								SignedPH = GetCheckBoxImage(IsChecked(cert["permitValidity"]?["signedPH"])),
								DateSignedPH = TemplateConvertString(cert["permitValidity"]?["dateSignedPH"]),
							},
						};
						break;
				}
			}
			else
			{
				html = html.Replace($"__FORM_{item.Description}__", "");
			}
		}

		List<dynamic> oiActivities = new();
		foreach (var item in opInfoActivities)
		{
			oiActivities.Add(new
			{
				Checkbox = GetCheckBoxImage((bool)item["status"]),
				Value = item["value"],
			});
		}

		List<dynamic> oiLocations = new();
		foreach (var item in opInfoLocations)
		{
			oiLocations.Add(new
			{
				Checkbox = GetCheckBoxImage((bool)item["status"]),
				Value = item["value"],
			});
		}

		List<dynamic> oiTools = new();
		foreach (var item in opInfoTools)
		{
			oiTools.Add(new
			{
				Checkbox = GetCheckBoxImage((bool)item["status"]),
				Value = item["value"],
			});
		}

		List<dynamic> oiProducts = new();
		foreach (var item in opInfoProducts)
		{
			oiProducts.Add(new
			{
				Id = item["id"],
				Checkbox = GetCheckBoxImage((bool)item["status"]),
				Value = item["value"],
			});
		}

		var appDomain = await _appSettings.GetValueAsync(companyId, "general", "application_domain");

		var templateData = new
		{
			AppDomain = appDomain,
			DateIssued = GeneralHelper.GetDateInTimeZone(permit.CreatedWhen).ToString("dd/MM/yyyy @ HH:mm"),
			PermitNo = string.Format("PTW{0:000000}", permit.RunningNumber),
			General = new
			{
				Location = TemplateConvertString(jsonObject["general"]["location"]["name"]),
				Description = TemplateConvertString(jsonObject["general"]["description"]),
				StartDate = TemplateConvertString(jsonObject["general"]["startDateTime"]),
				EndDate = TemplateConvertString(jsonObject["general"]["endDateTime"]),
			},
			PermitHolder = new
			{
				Name = TemplateConvertString(jsonObject["permitHolder"]["name"]),
				Company = TemplateConvertString(jsonObject["permitHolder"]["company"]),
				NumOfStaff = TemplateConvertString(jsonObject["permitHolder"]["numOfStaff"]),
				AdditionalStaff = TemplateConvertString(jsonObject["permitHolder"]["additionalStaff"]),
			},
			MainFormCertificates = mainFormCerts,
			AttachedCertificates = new
			{
				FormA = formA,
				FormB = formB,
				FormC = formC,
				FormD = formD,
				FormE = formE,
				FormF = formF,
				FormG = formG,
				FormH = formH,
			},
			OperationInfo = new
			{
				Activities = oiActivities,
				Locations = oiLocations,
				Tools = oiTools,
				Products = oiProducts,
			},
			OperationMeasures = new
			{
				Mechanical = new
				{
					EquipmentName = TemplateConvertString(jsonObject["operationMeasures"]["mechanical"]["equipmentName"]),
					TagNumber = TemplateConvertString(jsonObject["operationMeasures"]["mechanical"]["tagNumber"]),
					PressureFree = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["pressureFree"]["status"]),
					Empty = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["empty"]["status"]),
					Disconnected = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["disconnected"]["status"]),
					BlankOff = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["blankOff"]["status"]),
					LockedOff = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["lockedOff"]["status"]),
					Flushed = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["flushed"]["status"]),
					Ventilated = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["ventilated"]["status"]),
					Other = new
					{
						Checkbox = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["mechanical"]["other"]["status"]),
						Value = TemplateConvertString(jsonObject["operationMeasures"]["mechanical"]["other"]["value"], false, true),
					},
				},
				Electrical = new
				{
					EquipmentName = TemplateConvertString(jsonObject["operationMeasures"]["electrical"]["equipmentName"]),
					TagNumber = TemplateConvertString(jsonObject["operationMeasures"]["electrical"]["tagNumber"]),
					LockedOff = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["electrical"]["lockedOff"]["status"]),
					Isolated = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["electrical"]["isolated"]["status"]),
					IsolationCertificate = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["electrical"]["isolationCertificate"]["status"]),
					NumOfIsolationCertificate = TemplateConvertString(jsonObject["operationMeasures"]["electrical"]["numOfIsolationCertificate"]),
				},
				Instrument = new
				{
					EquipmentName = TemplateConvertString(jsonObject["operationMeasures"]["instrument"]["equipmentName"]),
					TagNumber = TemplateConvertString(jsonObject["operationMeasures"]["instrument"]["tagNumber"]),
					LockedOff = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["instrument"]["lockedOff"]["status"]),
					Isolated = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["instrument"]["isolated"]["status"]),
					Disconnected = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["instrument"]["disconnected"]["status"]),
					Bypass = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["instrument"]["bypass"]["status"]),
					ShutdownSystemOperational = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["instrument"]["shutdownSystemOperational"]["status"]),
					FireProtectionSystemOperational = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["instrument"]["fireProtectionSysOperational"]["status"]),
					AutoFireProtectionSystemOperational = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["instrument"]["autoFireProtectionSysOperational"]["status"]),
					NumOfIsolationCertificate = TemplateConvertString(jsonObject["operationMeasures"]["instrument"]["numOfIsolationCertificate"]),
					OtherMeasures = TemplateConvertString(jsonObject["operationMeasures"]["instrument"]["otherMeasures"]),
				},
				PtwHandOver = new
				{
					TerminalSafetyAwareness = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["pwtHandOver"]["terminalSafetyAwareness"]),
					PtwAwareness = GetCheckBoxImage((bool)jsonObject["operationMeasures"]["pwtHandOver"]["ptwAwareness"]),
				}
			},
			Ppe = new
			{
				SafetyGlass = GetCheckBoxImage((bool)jsonObject["ppe"]["safetyGlass"]["status"]),
				SafetyHelmet = GetCheckBoxImage((bool)jsonObject["ppe"]["safetyHelmet"]["status"]),
				FullFaceMask = GetCheckBoxImage((bool)jsonObject["ppe"]["fullFaceMask"]["status"]),
				LeatherGloves = GetCheckBoxImage((bool)jsonObject["ppe"]["leatherGloves"]["status"]),
				NeopreneGloves = GetCheckBoxImage((bool)jsonObject["ppe"]["neopreneGloves"]["status"]),
				SafetyHarness = GetCheckBoxImage((bool)jsonObject["ppe"]["safetyHarness"]["status"]),
				FallProtection = GetCheckBoxImage((bool)jsonObject["ppe"]["fallProtection"]["status"]),
				SafetyShoes = GetCheckBoxImage((bool)jsonObject["ppe"]["safetyShoes"]["status"]),
				EarProtection = GetCheckBoxImage((bool)jsonObject["ppe"]["earProtection"]["status"]),
				BreathingProtection = GetCheckBoxImage((bool)jsonObject["ppe"]["breathingProtection"]["status"]),
				OtherPpe = new
				{
					Checkbox = GetCheckBoxImage((bool)jsonObject["ppe"]["otherPpe"]["status"]),
					Value = TemplateConvertString(jsonObject["ppe"]["otherPpe"]["value"], false, true),
				},
				OtherRequiredInJHA = TemplateConvertString(jsonObject["ppe"]["otherPpeRequiredInJHA"]),
			},
			WorkArea = new
			{
				Demarcation = GetCheckBoxImage((bool)jsonObject["workArea"]["demarcation"]["status"]),
				FireExtinguisher = GetCheckBoxImage((bool)jsonObject["workArea"]["fireExtinguisher"]["status"]),
				WarningSigns = GetCheckBoxImage((bool)jsonObject["workArea"]["warningSigns"]["status"]),
				OtherPpeWorkArea = new
				{
					Checkbox = GetCheckBoxImage((bool)jsonObject["workArea"]["otherPpeWorkArea"]["status"]),
					Value = TemplateConvertString(jsonObject["workArea"]["otherPpeWorkArea"]["value"], false, true),
				},
			},
			Precautions = new
			{
				PrecautionsForWork = TemplateConvertString(jsonObject["precautions"]["precautionsForWork"]),
				AdditionalPrecautions = TemplateConvertString(jsonObject["precautions"]["additionalPrecautions"]),
				MethodOfStatementAttached = GetCheckBoxImage((bool)jsonObject["precautions"]["methodOfStatementAttached"]),
				Declaration = GetCheckBoxImage((bool)jsonObject["precautions"]["declaration"]),
				EngineeringAdviceRequired = GetCheckBoxImage((bool)jsonObject["precautions"]["engineeringAdviceRequired"]),
				PermitIssuerDeclaration = new
				{
					AuthorizedForExecution = GetCheckBoxImage((bool)jsonObject["precautions"]["permitIssuerDeclaration"]["authorizedForExecution"]),
				},
				LeadPermitIssuerDeclaration = new
				{
					AuthorizedForExecution = GetCheckBoxImage((bool)jsonObject["precautions"]["leadPermitIssuerDeclaration"]["authorizedForExecution"]),
				},
				PermitValidity = GetCheckBoxImage((bool)jsonObject["precautions"]["permitValidity"]),
			},
		};

		var template = Template.Parse(html);
		var renderedHtml = template.Render(templateData);

		byte[] htmlBytes = Encoding.UTF8.GetBytes(renderedHtml);
		string base64 = Convert.ToBase64String(htmlBytes);

		_logger.LogInformation("Generating PDF for permit {PermitId}", permit.Id);

		var browserFetcher = new BrowserFetcher();
		await browserFetcher.DownloadAsync();

		await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
		{
			Headless = true,
			Args = new[]
			{
				"--no-sandbox",                  // 👈 CRUCIAL: Bypasses kernel namespace blocks
				"--disable-setuid-sandbox",       // 👈 CRUCIAL: Disables helper binary constraints
				"--disable-dev-shm-usage",        // Prevents memory exhaustion crashes in Linux
				"--disable-crash-reporter"        // Prevents crashpad folder creation faults
			}
		});

		await using var page = await browser.NewPageAsync();
		await page.GoToAsync($"data:text/html;base64,{base64}");

		await page.EmulateMediaTypeAsync(MediaType.Print);

		await page.AddStyleTagAsync(new AddTagOptions
		{
			Url = $"https://{appDomain}/lib/bootstrap/css/bootstrap.min.css"
		});

		await page.AddStyleTagAsync(new AddTagOptions
		{
			Url = $"https://{appDomain}/lib/fontawesome/css/all.min.css"
		});

		await page.AddStyleTagAsync(new AddTagOptions
		{
			Url = $"https://{appDomain}/fonts/roboto.css"
		});

		pdfResult = await page.PdfDataAsync(new PdfOptions
		{
			Format = PaperFormat.A4,
			PrintBackground = true,
			MarginOptions = new MarginOptions
			{
				Bottom = "1cm",
				Left = "1.5cm",
				Right = "1.5cm",
				Top = "2.5cm"
			},
			DisplayHeaderFooter = true,
			HeaderTemplate = PdfHeaderTemplate(_webHostEnvironment.WebRootPath, permit.Company),
			FooterTemplate = PdfFooterTemplate(),
		});

		_logger.LogInformation("PDF generated successfully for permit {PermitId}, size {Bytes} bytes", permit.Id, pdfResult?.Length ?? 0);

		return pdfResult;
	}


	#region "Private static helpers"

	private static string PdfHeaderTemplate(string wwwroot, PermitPro.Core.Entities.Company companyInfo)
	{
		var logoPath = Path.Combine(wwwroot, "img", "app-logo.jpg");
		byte[] imageBytes = File.ReadAllBytes(logoPath);
		string base64 = Convert.ToBase64String(imageBytes);

		return "<div style=\"width:750px;\">\n" +
				"  <div style=\"display:flex;flex-direction:row;margin:0 auto;width:340px;\">\n" +
				$"    <div style=\"margin-right:10px;margin-top:0px;\"><img style=\"height:35px;width:auto;\" src=\"data:image/jpeg;base64,{base64}\"/></div>\n" +
				"    <div>\n" +
				$"      <div style=\"font-family:Arial;font-size:13pt;font-weight:bold;\">{companyInfo.Description.ToUpper()}</div>\n" +
				"      <div style=\"font-family:Arial;font-size:10pt;\">PERMIT TO WORK FORM</div>\n" +
				"    </div>\n" +
				"  </div>\n" +
				"</div>\n";
	}

	private static string PdfFooterTemplate()
	{
		return "<div style=\"text-align:right;width:800px;padding-right:30px;font-family:Arial;font-size:8pt;\">\n" +
				"<span class=\"pageNumber\"></span>/<span class=\"totalPages\"></span>" +
				"</div>\n";
	}

	private string GetCheckBoxImage(bool isChecked)
	{
		var companyId = _currentUserService.GetCurrentCompanyId();
		string appDomain = _appSettings.GetValueAsync(companyId, "general", "application_domain").GetAwaiter().GetResult();

		var checkBox = isChecked ? "mark" : "empty";
		return $"<img src=\"https://{appDomain}/img/cb-{checkBox}.png\" style=\"width:20px;\" />";
	}

	// Null-safe replacement for (bool) casts on certificate data. A certificate that was
	// attached but never filled in is stored as just {"name":"..."}, so any field may be missing.
	private static bool IsChecked(JToken token)
	{
		if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
			return false;

		if (token.Type == JTokenType.Boolean)
			return (bool)token;

		return bool.TryParse(token.ToString(), out var result) && result;
	}

	private static string TemplateConvertString(object data, bool useYesNo = false, bool isOtherValue = false)
	{
		var stringData = "&mdash;";

		try
		{
			var value = (JValue)data;

			if (value.Type != JTokenType.Null)
			{
				if (value.Type == JTokenType.Date)
					stringData = Convert.ToDateTime(value).ToLocalTime().ToString("dd/MM/yyyy");

				if (value.Type == JTokenType.String)
				{
					string tmp = value.ToString().Trim();
					stringData = (tmp == string.Empty) ? "&mdash;" : tmp;

					if (isOtherValue)
						stringData = (tmp == string.Empty) ? "&nbsp;" : $"({tmp})";

					stringData = stringData.Replace("\n", "<br>");
				}

				if (value.Type == JTokenType.Integer)
					stringData = value.ToString();

				if (value.Type == JTokenType.Boolean && useYesNo)
					stringData = Convert.ToBoolean(value) ? "YES" : "NO";
			}

			return stringData;
		}
		catch
		{
			return stringData;
		}
	}

	private static string GetCertificateHtmlTemplate(string wwwroot, string formName)
	{
		var htmlFile = Path.Combine(wwwroot, "templates", "html", "certificates", $"{formName}.html");
		return File.ReadAllText(htmlFile, Encoding.UTF8);
	}

	#endregion
}
