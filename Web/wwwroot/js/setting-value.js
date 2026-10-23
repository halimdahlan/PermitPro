// Value field of the setting modal (#stgValue) on the Settings, Global Settings and Company Settings
// pages: follows the chosen data type. "date" uses Flatpickr (needs _FlatpickrScripts) and saves
// yyyy-MM-dd, which DateTime.TryParse reads on any server culture (e.g. new_permit_form_cutover_date).
// "html" (email templates) swaps the field for a <textarea> that becomes a CodeMirror 5 HTML editor
// (loaded from jsDelivr on first use; plain textarea if it can't load) with a Code / Preview switch.
// The page sets SettingValue.previewUrl; the server renders the template with sample data, and the result
// is shown in a sandboxed iframe (no scripts).
const SettingValue = (() => {
	const CM = 'https://cdn.jsdelivr.net/npm/codemirror@5.65.16';
	let picker = null;
	let editor = null;        // CodeMirror instance while the type is "html"
	let loading = null;       // Promise while CodeMirror loads
	let tools = null;         // Code / Preview toolbar + iframe, built once
	let previewing = false;

	const loadScript = (src) => new Promise((resolve, reject) => {
		const s = document.createElement('script');
		s.src = src;
		s.onload = resolve;
		s.onerror = () => reject(new Error('Could not load ' + src));
		document.head.appendChild(s);
	});

	const loadCodeMirror = () => {
		if (window.CodeMirror) return Promise.resolve();
		if (!loading) {
			const css = document.createElement('link');
			css.rel = 'stylesheet';
			css.href = CM + '/lib/codemirror.min.css';
			document.head.appendChild(css);

			loading = [
				'/lib/codemirror.min.js',
				'/mode/xml/xml.min.js',
				'/mode/javascript/javascript.min.js',
				'/mode/css/css.min.js',
				'/mode/htmlmixed/htmlmixed.min.js',
			].reduce((p, path) => p.then(() => loadScript(CM + path)), Promise.resolve())
				.catch((e) => { loading = null; throw e; });
		}
		return loading;
	};

	const injectStyles = () => {
		if (document.getElementById('stgHtmlStyles')) return;
		const style = document.createElement('style');
		style.id = 'stgHtmlStyles';
		style.textContent = `
			.stg-html .CodeMirror { height: 420px; border: 1px solid var(--bs-border-color); border-radius: .375rem; font-size: .8rem;
				background: var(--bs-body-bg); color: var(--bs-body-color); }
			.stg-html .CodeMirror-gutters { background: var(--bs-tertiary-bg); border-right: 1px solid var(--bs-border-color); }
			.stg-html .CodeMirror-cursor { border-left-color: var(--bs-body-color); }
			.stg-html .CodeMirror-focused .CodeMirror-selected, .stg-html .CodeMirror-selected { background: rgba(31,111,209,.25); }
			.stg-html .stg-cm-disabled { opacity: .6; }
			[data-bs-theme="dark"] .stg-html .cm-tag { color: #7aa7ff; }
			[data-bs-theme="dark"] .stg-html .cm-attribute { color: #9bd1ff; }
			[data-bs-theme="dark"] .stg-html .cm-string { color: #f2a07b; }
			[data-bs-theme="dark"] .stg-html .cm-comment { color: #7f9a7f; }
			[data-bs-theme="dark"] .stg-html .cm-meta, [data-bs-theme="dark"] .stg-html .cm-bracket { color: #aab4c4; }
			[data-bs-theme="dark"] .stg-html .cm-number, [data-bs-theme="dark"] .stg-html .cm-atom { color: #c7a6ff; }
			.stg-html .stg-preview-frame { width: 100%; height: 480px; border: 1px solid var(--bs-border-color); border-radius: .375rem;
				background: #fff; display: block; margin: 0 auto; }
			.stg-html .stg-preview-frame.phone { max-width: 375px; }`;
		document.head.appendChild(style);
	};

	// The toolbar and iframe go around the value field the first time "html" is chosen.
	const buildTools = (field) => {
		injectStyles();
		const wrap = field.parentElement;
		wrap.classList.add('stg-html');

		const bar = document.createElement('div');
		bar.className = 'd-flex flex-wrap align-items-center gap-2 mb-2';
		bar.innerHTML = `
			<div class="btn-group btn-group-sm" role="group">
				<button type="button" class="btn btn-outline-secondary active" data-mode="code"><i class="fa-regular fa-code me-1"></i>Code</button>
				<button type="button" class="btn btn-outline-secondary" data-mode="preview"><i class="fa-regular fa-eye me-1"></i>Preview</button>
			</div>
			<div class="form-check form-switch mb-0 d-none" data-phone>
				<input class="form-check-input" type="checkbox" id="stgPreviewPhone" />
				<label class="form-check-label small" for="stgPreviewPhone">Phone width</label>
			</div>
			<span class="small text-danger" data-msg></span>`;

		const frame = document.createElement('iframe');
		frame.className = 'stg-preview-frame d-none';
		// No scripts, no same-origin access: a template can't run code on this page.
		frame.setAttribute('sandbox', '');
		frame.title = 'Email preview';

		const hint = document.getElementById('stgValueHint');
		wrap.insertBefore(bar, wrap.querySelector('label').nextSibling);
		wrap.insertBefore(frame, hint);

		bar.querySelectorAll('[data-mode]').forEach(b => b.addEventListener('click', () => setMode(b.dataset.mode)));
		bar.querySelector('#stgPreviewPhone').addEventListener('change', (e) => frame.classList.toggle('phone', e.target.checked));

		tools = { bar, frame, msg: bar.querySelector('[data-msg]'), phone: bar.querySelector('[data-phone]') };
	};

	const setMode = async (mode) => {
		const field = document.getElementById('stgValue');
		const { bar, frame, msg, phone } = tools;
		msg.textContent = '';

		if (mode === 'preview') {
			try {
				const res = await fetch(SettingValue.previewUrl, {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({ key: document.getElementById('stgKey').value, html: field.value }),
				});
				const data = await res.json().catch(() => ({}));
				if (!res.ok) { msg.textContent = data.error || 'The preview could not be built.'; return; }
				frame.srcdoc = data.html;
			} catch (e) {
				msg.textContent = 'The preview could not be built.';
				return;
			}
		}

		previewing = mode === 'preview';
		frame.classList.toggle('d-none', !previewing);
		phone.classList.toggle('d-none', !previewing);
		(editor ? editor.getWrapperElement() : field).classList.toggle('d-none', previewing);
		bar.querySelectorAll('[data-mode]').forEach(b => b.classList.toggle('active', b.dataset.mode === mode));
		if (!previewing && editor) editor.refresh();
	};

	const showTools = (visible) => {
		if (!tools) return;
		tools.bar.classList.toggle('d-none', !visible);
		if (!visible) {
			tools.frame.classList.add('d-none');
			tools.frame.srcdoc = '';
		}
	};

	// <input> for most types, <textarea> for "html": swap #stgValue, keeping its id, value and disabled
	// state so the pages' $('#stgValue') code keeps working.
	const useTag = (tag) => {
		const current = document.getElementById('stgValue');
		if (current.tagName.toLowerCase() === tag) return current;

		const next = document.createElement(tag);
		next.id = 'stgValue';
		next.className = 'form-control';
		next.value = current.value;
		next.disabled = current.disabled;
		if (tag === 'textarea') {
			next.rows = 14;
			next.wrap = 'off';
			next.spellcheck = false;
			next.style.fontFamily = 'Consolas, "Courier New", monospace';
			next.style.fontSize = '.8rem';
		} else {
			next.type = 'text';
		}
		current.replaceWith(next);
		return next;
	};

	// Leaves CodeMirror. The textarea's own value is the truth here (the page may have just set it with
	// .val() while the editor still showed the previous setting), and toTextArea() would overwrite it.
	const detachEditor = () => {
		if (!editor) return;
		const field = document.getElementById('stgValue');
		const value = field.value;
		editor.toTextArea();
		field.value = value;
		editor = null;
	};

	const attachEditor = (field) => {
		loadCodeMirror().then(() => {
			// The type may have changed while it loaded.
			const current = document.getElementById('stgValue');
			if (current !== field || current.tagName.toLowerCase() !== 'textarea' || editor) return;

			editor = CodeMirror.fromTextArea(field, { mode: 'htmlmixed', lineNumbers: true, lineWrapping: true, indentUnit: 4, tabSize: 4, indentWithTabs: true });
			editor.on('change', () => editor.save());   // keeps the textarea (what the page reads) current
			refresh();
			// Opened while the modal is still hidden: size it once it shows.
			setTimeout(() => editor && editor.refresh(), 0);
		}).catch(() => { /* plain textarea stays */ });
	};

	// Call after the page enables/disables #stgValue ("use global value").
	const refresh = () => {
		if (!editor) return;
		const disabled = document.getElementById('stgValue').disabled;
		editor.setOption('readOnly', disabled ? 'nocursor' : false);
		editor.getWrapperElement().classList.toggle('stg-cm-disabled', disabled);
	};

	// One-line preview for the settings grids, so a long value (an email template) doesn't fill the row.
	const preview = (value, max = 100) => {
		const text = String(value ?? '').replace(/\s+/g, ' ').trim();
		return text.length > max ? text.slice(0, max) + '…' : text;
	};

	// `value` (optional): the value to show. Pages must pass it here rather than set it with .val() first:
	// while the field is still an <input>, the browser strips the line breaks out of a long html value.
	const setType = (dataType, value) => {
		if (dataType !== 'date' && picker) {
			picker.destroy();
			picker = null;
		}

		const isHtml = dataType === 'html';
		const dialog = document.getElementById('stgValue').closest('.modal-dialog');
		if (dialog) dialog.classList.toggle('modal-xl', isHtml);

		if (!isHtml) {
			if (previewing && tools) setMode('code');
			detachEditor();
			showTools(false);
		}

		const input = useTag(isHtml ? 'textarea' : 'input');
		if (value !== undefined) input.value = value;

		if (isHtml) {
			if (!tools) buildTools(input);
			showTools(true);
			setMode('code');
			if (editor) {
				editor.setValue(input.value);   // the page set the new value on the textarea
				editor.clearHistory();
				refresh();
			} else {
				attachEditor(input);
			}
			return;
		}

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

		input.type = dataType === 'password' ? 'password' : 'text';
	};

	// CodeMirror can't measure itself while the modal is hidden.
	document.addEventListener('shown.bs.modal', () => { if (editor && !previewing) editor.refresh(); });

	return { setType, preview, refresh, previewUrl: '' };
})();
