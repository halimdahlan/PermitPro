// Value field of the setting modal (#stgValue) on the Settings, Global Settings and Company Settings
// pages: follows the chosen data type. "date" uses Flatpickr (needs _FlatpickrScripts) and saves
// yyyy-MM-dd, which DateTime.TryParse reads on any server culture (e.g. new_permit_form_cutover_date).
const SettingValue = (() => {
	let picker = null;

	const setType = (dataType) => {
		const input = document.getElementById('stgValue');

		if (dataType === 'date') {
			input.type = 'text';
			if (!picker) {
				picker = flatpickr(input, {
					dateFormat: 'Y-m-d',
					allowInput: true,
					// Inside the field's wrapper, so the calendar stays within the Bootstrap modal.
					static: true,
				});
				// The static wrapper is inline-block by default; keep the field full width under its label.
				input.parentElement.style.display = 'block';
			}
			picker.setDate(input.value || null, false, 'Y-m-d');
			return;
		}

		if (picker) {
			picker.destroy();
			picker = null;
		}
		input.type = dataType === 'password' ? 'password' : 'text';
	};

	return { setType };
})();
