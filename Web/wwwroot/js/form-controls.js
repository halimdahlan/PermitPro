/*
 * Native replacements for the Kendo widgets on the old-system permit form (Permits/New, Permits/Edit).
 *
 * Every element marked data-control="..." gets an object at $(el).data('control') with the methods
 * the page code used on the Kendo widget (value, enable, focus, min, max, text, check, trigger).
 * value() returns exactly what Kendo returned (booleans, null for untouched fields, Date objects), so
 * the saved PermitForm JSON - and the old-system PDF built from it - does not change.
 *
 *   checkbox / switch   value() -> true/false; accepts true/false or "true"/"false"
 *   textbox / textarea  value() -> whatever was last set (null stays null) until the user edits it
 *   numeric             value() -> number or null; typed values are clamped to min/max
 *   date / datetime / time   value() -> Date or null (Flatpickr)
 *   tree                value() -> location id or ''; text() -> location name (Treeselect)
 *
 * Change handlers are named in the markup (data-change="fnName") and registered by the page with
 * FormControls.handlers({ fnName }), because the page functions are top-level consts, not window
 * properties. Handlers receive { sender: control }, like Kendo's change event.
 */
window.FormControls = (() => {
	const handlers = {};

	const fireChange = (ctl) => {
		const name = ctl.element.attr('data-change');
		if (!name) return;

		const fn = handlers[name];
		if (fn) {
			fn({ sender: ctl });
		} else {
			console.error(`FormControls: no change handler registered as "${name}".`);
		}
	};

	class Control {
		constructor(el) {
			this.element = $(el);
		}

		// enable() and enable(true) enable; enable(false) disables (Kendo semantics).
		enable(enabled) {
			this.element.prop('disabled', enabled === false);
		}

		readonly(readonly) {
			this.element.prop('readOnly', readonly !== false);
		}

		focus() {
			this.element.trigger('focus');
		}

		trigger(eventName) {
			if (eventName === 'change') fireChange(this);
		}
	}

	// Checkbox and switch.
	class Toggle extends Control {
		constructor(el) {
			super(el);
			el.addEventListener('change', () => fireChange(this));
		}

		value(value) {
			if (value === undefined) return this.element[0].checked;
			if (typeof value === 'string') value = value === 'true';
			this.element[0].checked = !!value;
		}

		check(value) {
			return this.value(value);
		}
	}

	// Kendo's TextBox keeps the last value set through value() (null, numbers...) and only
	// replaces it with the input's text when the user changes the field.
	class TextBox extends Control {
		constructor(el) {
			super(el);
			this._value = el.value;
			el.addEventListener('change', () => {
				this._value = el.value;
				fireChange(this);
			});
		}

		value(value) {
			if (value === undefined) return this._value;
			this._value = value;
			this.element.val(value);
		}
	}

	// Kendo's TextArea takes the text on blur, when it differs from the stored value.
	class TextArea extends TextBox {
		constructor(el) {
			super(el);
			el.addEventListener('focusout', () => {
				const stored = this._value === null ? '' : this._value;
				if (stored !== el.value) this._value = el.value;
			});
		}
	}

	class Numeric extends Control {
		constructor(el) {
			super(el);
			this._value = this._parse(el.value);
			el.addEventListener('change', () => {
				let value = this._parse(el.value);
				if (value !== null) value = Math.min(Math.max(value, this._min()), this._max());
				this._set(value);
				fireChange(this);
			});
		}

		_min() {
			return this.element[0].min === '' ? -Infinity : Number(this.element[0].min);
		}

		_max() {
			return this.element[0].max === '' ? Infinity : Number(this.element[0].max);
		}

		// Kendo keeps decimals even with Format("0").
		_parse(value) {
			if (value === null || value === undefined || String(value).trim() === '') return null;
			const number = Number(value);
			return isNaN(number) ? null : number;
		}

		_set(value) {
			this._value = value;
			this.element.val(value === null ? '' : value);
		}

		// Typed values are clamped to min/max (above); a value set in code outside them clears the field.
		value(value) {
			if (value === undefined) return this._value;
			value = this._parse(value);
			if (value !== null && (value < this._min() || value > this._max())) value = null;
			this._set(value);
		}
	}

	const DATE_FORMATS = {
		date: 'd/m/Y',
		datetime: 'd/m/Y h:i K',
		time: 'h:i K',
	};

	// Accepts a Date, an ISO string, a string in the picker's display format, or "now"/"today".
	const toDate = (value, format) => {
		if (value === null || value === undefined || value === '') return null;
		if (value instanceof Date) return value;
		if (value === 'now') return new Date();
		if (value === 'today') {
			const d = new Date();
			d.setHours(0, 0, 0, 0);
			return d;
		}

		const parsed = flatpickr.parseDate(String(value), format);
		if (parsed && !isNaN(parsed)) return parsed;

		const fallback = new Date(value);
		return isNaN(fallback) ? null : fallback;
	};

	class DateControl extends Control {
		constructor(el, mode) {
			super(el);
			this._format = DATE_FORMATS[mode];

			const button = this._addButton(mode === 'time' ? 'fa-clock' : 'fa-calendar');

			const data = el.dataset;
			this.picker = flatpickr(el, {
				dateFormat: this._format,
				allowInput: true,
				enableTime: mode !== 'date',
				noCalendar: mode === 'time',
				time_24hr: false,
				minuteIncrement: 1,
				defaultDate: toDate(data.value, this._format),
				minDate: toDate(data.min, this._format),
				maxDate: toDate(data.max, this._format),
				// Clicking the button must not count as a click outside the picker.
				ignoredFocusElements: [button],
				onChange: () => fireChange(this),
			});
		}

		// Calendar/clock button after the field, like Kendo's pickers had.
		_addButton(icon) {
			const field = this.element.closest('.form-floating')[0] || this.element[0];
			const group = document.createElement('div');
			group.className = 'input-group fc-date';
			field.parentNode.insertBefore(group, field);
			group.appendChild(field);

			const button = document.createElement('button');
			button.type = 'button';
			button.className = 'input-group-text';
			button.tabIndex = -1;
			button.setAttribute('aria-label', icon === 'fa-clock' ? 'Choose time' : 'Choose date');
			button.innerHTML = `<i class="fa-regular ${icon}"></i>`;
			button.addEventListener('click', () => {
				if (!this.element.prop('disabled')) this.picker.toggle();
			});
			group.appendChild(button);
			return button;
		}

		value(value) {
			if (value === undefined) return this.picker.selectedDates[0] ?? null;

			const date = toDate(value, this._format);
			if (date) {
				this.picker.setDate(date, false);
			} else {
				this.picker.clear(false);
			}
		}

		min(value) {
			if (value === undefined) return this.picker.config.minDate ?? null;
			this.picker.set('minDate', toDate(value, this._format));
		}

		max(value) {
			if (value === undefined) return this.picker.config.maxDate ?? null;
			this.picker.set('maxDate', toDate(value, this._format));
		}

		enable(enabled) {
			super.enable(enabled);
			if (this.picker.altInput) this.picker.altInput.disabled = enabled === false;
		}
	}

	// Single-choice location tree. The page may set a value before the tree has loaded.
	class Tree extends Control {
		constructor(el) {
			super(el);
			this._value = el.dataset.value || '';
			this._options = [];
			this.tree = null;

			fetch(el.dataset.url, { headers: { 'Accept': 'application/json' } })
				.then(r => { if (!r.ok) throw new Error(r.statusText); return r.json(); })
				.then(options => {
					this._options = options;
					this.tree = new Treeselect({
						parentHtmlContainer: el,
						options: options,
						value: this._value || null,
						isSingleSelect: true,
						showTags: false,
						placeholder: el.dataset.placeholder || '(select)',
						emptyText: 'No locations found.',
						inputCallback: (value) => {
							this._value = value ?? '';
							fireChange(this);
						},
					});
				})
				.catch(() => {
					el.innerHTML = '<div class="form-control text-danger">Could not load locations.</div>';
				});
		}

		value(value) {
			if (value === undefined) return this._value;
			this._value = value ?? '';
			this.tree?.updateValue(this._value || null);
		}

		text() {
			const find = (nodes) => {
				for (const node of nodes) {
					if (String(node.value).toLowerCase() === String(this._value).toLowerCase()) return node.name;
					const found = find(node.children || []);
					if (found !== null) return found;
				}
				return null;
			};

			return this._value ? (find(this._options) ?? '') : '';
		}
	}

	const TYPES = {
		checkbox: (el) => new Toggle(el),
		switch: (el) => new Toggle(el),
		textbox: (el) => new TextBox(el),
		textarea: (el) => new TextArea(el),
		numeric: (el) => new Numeric(el),
		date: (el) => new DateControl(el, 'date'),
		datetime: (el) => new DateControl(el, 'datetime'),
		time: (el) => new DateControl(el, 'time'),
		tree: (el) => new Tree(el),
	};

	const initialized = new WeakSet();

	const init = (root = document) => {
		root.querySelectorAll('[data-control]').forEach(el => {
			// Not $(el).data('control'): before init jQuery returns the data-control attribute text.
			if (initialized.has(el)) return;
			initialized.add(el);

			const create = TYPES[el.dataset.control];
			if (!create) {
				console.error(`FormControls: unknown control type "${el.dataset.control}" on #${el.id}.`);
				return;
			}

			$(el).data('control', create(el));
		});
	};

	return {
		init,
		handlers: (fns) => Object.assign(handlers, fns),
	};
})();

// The markup is already in place when this script runs (it is loaded at the end of the page),
// so the controls exist before any $(document).ready code reads them.
FormControls.init();
