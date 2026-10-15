var PermitModule = (() => {

	var _companyId;
	var _permitId;
	var _errorMessages = [];
	var _errorDialog = null;

	var _permitInfo = {
		general: {
			location: {
				id: null,
				name: null,
			},
			description: null,
			startDateTime: null,
			endDateTime: null,
			certificates: [],
		},
		operation: {
			activities: [],
			activityLocations: [],
			activityTools: [],
			activityProducts: [],
		},
		permitHolder: {
			name: null,
			company: null,
			numOfStaff: 0,
			additionalStaff: null,
		},
		operationMeasures: {
			mechanical: {
				equipmentName: null,
				tagNumber: null,
				pressureFree: null,
				empty: null,
				disconnected: null,
				blankOff: null,
				lockedOff: null,
				flushed: null,
				ventilated: null,
				other: null,
			},
			electrical: {
				equipmentName: null,
				tagNumber: null,
				lockedOff: false,
				isolated: false,
				isolationCertificate: false,
				numOfIsolationCertificate: 0,
			},
			instrument: {
				equipmentName: null,
				tagNumber: null,
				lockedOff: null,
				isolated: null,
				disconnected: null,
				bypass: null,
				numOfIsolationCertificate: 0,
				shutdownSystemOperational: null,
				fireProtectionSysOperational: null,
				autoFireProtectionSysOperational: null,
				otherMeasures: null,
			},
			pwtHandOver: {
				terminalSafetyAwareness: false,
				ptwAwareness: false,
			},
		},
		ppe: {
			safetyGlass: null,
			safetyHelmet: null,
			fullFaceMask: null,
			leatherGloves: null,
			neopreneGloves: null,
			safetyHarness: null,
			fallProtection: null,
			safetyShoes: null,
			earProtection: null,
			breathingProtection: null,
			otherPpe: null,
			otherPpeRequiredInJHA: null,
		},
		workArea: {
			demarcation: null,
			fireExtinguisher: null,
			warningSigns: null,
			otherPpeWorkArea: null,
		},
		precautions: {
			precautionsForWork: null,
			additionalPrecautions: null,
			methodOfStatementAttached: false,
			declaration: false,
			engineeringAdviceRequired: false,
			permitIssuerDeclaration: {
				authorizedForExecution: false,
			},
			leadPermitIssuerDeclaration: {
				authorizedForExecution: false,
			},
			permitValidity: false,
		}
	};

	return {

		setErrorDialog: (dialog) => {
			_errorDialog = dialog;
			return;
		},

		companyId: (id) => {
			_companyId = id;
			return;
		},

		permitId: (id) => {
			_permitId = id;
			return;
		},

		permitInfo: (info) => {
			if (info != null) _permitInfo = info;
			return _permitInfo;
		},

		/**
		 * submit
		 * @returns
		 */
		submit: (submissionType) => {
			const locationId = $('#ddtLocation').data('control').value();
			const startDateTime = $('#startDateTime').data('control').value();
			const endDateTime = $('#endDateTime').data('control').value();

			_errorMessages = [];

			if (locationId == '') {
				_errorMessages.push('Location is required');
			}

			if (startDateTime == null) {
				_errorMessages.push('Start Date is required');
			}

			if (endDateTime == null) {
				_errorMessages.push('End Date is required');
			}

			if (_errorMessages.length > 0) {
				return false;
			}

			_permitInfo.general.location.id = $('#ddtLocation').data('control').value();
			_permitInfo.general.location.name = $('#ddtLocation').data('control').text();
			_permitInfo.general.description = $('#tbPermitDesc').data('control').value();
			_permitInfo.general.startDateTime = $('#startDateTime').data('control').value();
			_permitInfo.general.endDateTime = $('#endDateTime').data('control').value();

			_permitInfo.operation.activities = [];

			for (var counter = 1; counter < 20; counter++) {
				const elemId = '#chkOpInfo' + counter;
				const value = $(elemId).data('control').value();

				let tbx = null;

				if (counter == 19) {
					tbx = $('#tbOpInfo19').data('control').value();
				}

				_permitInfo.operation.activities.push({
					id: elemId,
					status: value,
					value: tbx,
				});
			}

			_permitInfo.operation.activityLocations = [];

			for (var counter = 20; counter < 24; counter++) {
				const elemId = '#chkOpInfo' + counter;
				const value = $(elemId).data('control').value();

				_permitInfo.operation.activityLocations.push({
					id: elemId,
					status: value,
					value: null,
				});
			}

			_permitInfo.operation.activityTools = [];

			for (var counter = 24; counter < 28; counter++) {
				const elemId = '#chkOpInfo' + counter;
				const value = $(elemId).data('control').value();

				_permitInfo.operation.activityTools.push({
					id: elemId,
					status: value,
					value: null,
				});
			}

			_permitInfo.operation.activityTools.push({
				id: '#chkOpInfo27a',
				status: $('#chkOpInfo27a').data('control').value(),
				value: $('#tbOpInfo27a').data('control').value(),
			});

			_permitInfo.operation.activityProducts = [];

			for (var counter = 28; counter < 32; counter++) {
				const elemId = '#chkOpInfo' + counter;
				const value = $(elemId).data('control').value();

				let tbx = null;

				if (counter == 31) {
					tbx = $('#tbOpInfo31').data('control').value();
				}

				_permitInfo.operation.activityProducts.push({
					id: elemId,
					status: value,
					value: tbx,
				});
			}

			_permitInfo.permitHolder.name = $('#tbPermitHolderInfo1').data('control').value();
			_permitInfo.permitHolder.company = $('#tbPermitHolderInfo2').data('control').value();
			_permitInfo.permitHolder.numOfStaff = $('#tbPermitHolderInfo3').data('control').value();
			_permitInfo.permitHolder.additionalStaff = $('#tbPermitHolderInfo4').data('control').value();

			for (var counter = 1; counter < 12; counter++) {
				const elemId = '#swPpeWorkArea' + counter;
				const value = $(elemId).data('control').value();
				const dataElem = $(elemId).data('elem');

				_permitInfo.ppe[dataElem] = { status: value, value: null };

				if (dataElem == 'otherPpe') {
					_permitInfo.ppe[dataElem].value = $('#tbPpeWorkArea11').data('control').value();
				}
			}

			_permitInfo.ppe.otherPpeRequiredInJHA = $('#otherPpeRequiredInJHA').data('control').value();


			for (var counter = 12; counter < 16; counter++) {
				const elemId = '#swPpeWorkArea' + counter;
				const value = $(elemId).data('control').value();
				const dataElem = $(elemId).data('elem');

				_permitInfo.workArea[dataElem] = { status: value, value: null };

				if (dataElem == 'otherPpeWorkArea') {
					_permitInfo.workArea[dataElem].value = $('#tbPpeWorkArea15').data('control').value();
				}
			}


			_permitInfo.operationMeasures.mechanical.equipmentName = $('#tbOpMeasureMech1').data('control').value();
			_permitInfo.operationMeasures.mechanical.tagNumber = $('#tbOpMeasureMech2').data('control').value();

			for (var counter = 1; counter < 9; counter++) {
				const elemId = '#swOpMeasureMech' + counter;
				const value = $(elemId).data('control').value();
				const dataElem = $(elemId).data('elem');

				if (dataElem == 'other') {
					_permitInfo.operationMeasures.mechanical[dataElem] = { status: value, value: $('#tbOpMeasureMech8').data('control').value() };
				} else {
					_permitInfo.operationMeasures.mechanical[dataElem] = { status: value, value: null };
				}
			}


			_permitInfo.operationMeasures.electrical.equipmentName = $('#tbOpMeasureElec1').data('control').value();
			_permitInfo.operationMeasures.electrical.tagNumber = $('#tbOpMeasureElec2').data('control').value();
			_permitInfo.operationMeasures.electrical.numOfIsolationCertificate = $('#numOpMeasureElec1').data('control').value();

			for (var counter = 1; counter < 4; counter++) {
				const elemId = '#swOpMeasureElec' + counter;
				const value = $(elemId).data('control').value();
				const dataElem = $(elemId).data('elem');

				if (dataElem == 'other') {
					_permitInfo.operationMeasures.electrical[dataElem] = { status: value, value: $('#tbOpMeasureElec' + counter).data('control').value() };
				} else {
					_permitInfo.operationMeasures.electrical[dataElem] = { status: value, value: null };
				}
			}


			_permitInfo.operationMeasures.instrument.equipmentName = $('#tbOpMeasureInst1').data('control').value();
			_permitInfo.operationMeasures.instrument.tagNumber = $('#tbOpMeasureInst2').data('control').value();
			_permitInfo.operationMeasures.instrument.numOfIsolationCertificate = $('#numOpMeasureInst1').data('control').value();
			_permitInfo.operationMeasures.instrument.otherMeasures = $('#txtOpMeasureInst1').data('control').value();

			for (var counter = 1; counter < 8; counter++) {
				const elemId = '#swOpMeasureInst' + counter;
				const value = $(elemId).data('control').value();
				const dataElem = $(elemId).data('elem');

				_permitInfo.operationMeasures.instrument[dataElem] = { status: value, value: null };
			}


			_permitInfo.operationMeasures.pwtHandOver.terminalSafetyAwareness = $('#swPpeWorkAreaInst8').data('control').value();
			_permitInfo.operationMeasures.pwtHandOver.ptwAwareness = $('#swPpeWorkAreaInst9').data('control').value();

			_permitInfo.precautions.precautionsForWork = $('#precautionsForWork').data('control').value();
			_permitInfo.precautions.additionalPrecautions = $('#additionalPrecautions').data('control').value();
			_permitInfo.precautions.methodOfStatementAttached = $('#methodOfStatementAttached').data('control').value();
			_permitInfo.precautions.engineeringAdviceRequired = $('#engineeringAdviceRequired').data('control').value();
			_permitInfo.precautions.declaration = $('#declaration').data('control').value();
			_permitInfo.precautions.permitIssuerDeclaration.authorizedForExecution = $('#authorizedForExecution1').data('control').value();
			_permitInfo.precautions.leadPermitIssuerDeclaration.authorizedForExecution = $('#authorizedForExecution2').data('control').value();
			_permitInfo.precautions.permitValidity = $('#permitValidity').data('control').value();

			var formData = new FormData();

			formData.append('PermitForm', JSON.stringify(_permitInfo));
			formData.append('SubmissionType', submissionType);
			formData.append('CompanyId', _companyId);

			$('.loading').show();

			let requestMethod = 'POST';

			if (_permitId != null) {
				requestMethod = 'PUT';
				formData.append('PermitId', _permitId);
			}

			const xhr = $.ajax({
				method: requestMethod,
				type: requestMethod,
				url: companyBase + '/permits',
				data: formData,
				contentType: false,
				processData: false,
				cache: false,
			});

			xhr.done((response, status) => {
				if (response.data == 'OK') {
					document.location.href = companyBase + '/permits';
				}
			});

			xhr.fail((response, status) => {
				console.log(status);
			});

			xhr.always(() => {
				$('.loading').hide();
			});

			return true;
		},

		updateCertificate: () => {
			var formData = new FormData();

			formData.append('PermitForm', JSON.stringify(_permitInfo));
			formData.append('CompanyId', _companyId);
			formData.append('PermitId', _permitId);

			$('.loading').show();

			let requestMethod = 'PUT';

			const xhr = $.ajax({
				method: requestMethod,
				type: requestMethod,
				url: companyBase + '/permits/certs',
				data: formData,
				contentType: false,
				processData: false,
				cache: false,
			});

			xhr.done((response, status) => {
				if (response.data == 'OK') {
					//document.location.href = companyBase + '/permits';
					console.log('OK');
				}
			});

			xhr.fail((response, status) => {
				console.log(status);
			});

			xhr.always(() => {
				$('.loading').hide();
			});

			return true;
		},

		getErrorMessages: () => {
			if (_errorMessages.length > 0) {
				$('#dlgInputError').modal('show');
				$('#divInputErrors').empty();
	
				const ul = document.createElement('ul');
				ul.classList.add('text-danger');
				ul.classList.add('mb-0');
	
				$.each(_errorMessages, (i, v) => {
					ul.appendChild(document.createElement('li')).appendChild(document.createTextNode(v));
				});
	
				$('#divInputErrors').append(ul);
			}

			return;
		},

		onCertificateCheckboxChange: (e) => {
			const elemData = $(e.sender.element).data('certname');
			const data = elemData.split('|');
			const switchState = e.sender.value();

			const elem = e.sender.element[0];
			const elemId = elem.id;

			let elemIndex = -1;

			_permitInfo.general.certificates.forEach((item, index) => {
				if (item == elemId) {
					elemIndex = index
				}
			});

			if (e.sender.value()) {
				if (elemIndex == -1) {
					_permitInfo.general.certificates.push(elemId);
				}
			} else {
				if (elemIndex > -1) {
					_permitInfo.general.certificates.splice(elemIndex, 1);
				}
			}

			if (selectedCerts.length == 0) {
				$('ul.selected-certs').empty();
			}

			if (switchState) {
				constructItem(data);
			} else {
				removeCert(data[0]);
			}

			return;
		},

	};

})();