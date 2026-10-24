/*
 * Shared setup for list-page grids (DataTables 2, replacing the Kendo Grid).
 * Load after jQuery, moment and DataTables (see Views/Shared/_DataGridScripts.cshtml).
 *
 * Markup: <div class="data-grid"> containing a <div class="grid-error ..."> and the <table>.
 */
window.DataGrid = (() => {

	const antiForgeryToken = () => $('input:hidden[name="__RequestVerificationToken"]').val();

	const escapeHtml = (value) => $('<div>').text(value ?? '').html();

	// Plain text column: escaped for display, raw value for search and sort.
	const renderText = (data, type) => type === 'display' ? escapeHtml(data) : (data ?? '');

	// Shows a formatted date; sorting and searching use the raw ISO value.
	const renderDate = (data, type) => {
		if (type !== 'display') return data ?? '';
		return data ? moment(data).format('DD MMM, YYYY') : '&mdash;';
	};

	// Active/inactive icon for display; "Active"/"Inactive" for search, sort and filters.
	const renderActive = (data, type) => {
		if (type === 'display') {
			return data
				? '<i class="fa-sharp fa-solid fa-circle-check fa-lg text-success" title="Active"></i>'
				: '<i class="fa-sharp fa-solid fa-circle-check fa-lg text-warning" title="Inactive"></i>';
		}
		return data ? 'Active' : 'Inactive';
	};

	// Server-built HTML column (icons, badges): shown as-is, never searched or sorted.
	const htmlColumn = (data, extra) => Object.assign({
		data: data,
		orderable: false,
		searchable: false,
	}, extra);

	const defaults = {
		// Keep the server order until the user sorts a column.
		order: [],
		pageLength: 10,
		lengthMenu: [10, 20, 50, 100],
		autoWidth: false,
		// Search top right; paging bottom left; row count and page size bottom right.
		layout: {
			topStart: null,
			topEnd: 'search',
			bottomStart: 'paging',
			bottomEnd: ['info', 'pageLength'],
		},
		language: {
			search: '',
			searchPlaceholder: 'Search...',
			emptyTable: 'No records found.',
			zeroRecords: 'No matching records found.',
			loadingRecords: '<i class="fa-solid fa-loader fa-spin me-2"></i> Loading...',
		},
	};

	// Phones show each row as a card (datagrid.css, .dt-cards): every cell is labelled with its
	// column title. Action cells (an "Action(s)" or empty header) get no label.
	const labelCells = (tbl) => {
		tbl.cells({ page: 'current' }).every(function () {
			const node = this.node();
			if (!node) return;
			const title = $(tbl.column(this.index().column).header()).text().trim();
			const isAction = title === '' || /^actions?$/i.test(title);
			node.setAttribute('data-label', isAction ? '' : title);
			node.classList.toggle('dt-card-actions', isAction);
			// Nothing to show (e.g. no certificates): hide the line on a card.
			node.classList.toggle('dt-card-empty', node.textContent.trim() === '' && !node.querySelector('img, i, svg, input, select, button'));
		});
	};

	// Card rows on phones for a plain HTML table (not a DataTable), e.g. one built from a template
	// string: labels every body cell from its column header. Call after (re)rendering the rows.
	// The table must sit inside a .data-grid element.
	const labelTable = (table) => {
		const titles = $(table).find('thead th').map((i, th) => $(th).text().trim()).get();
		$(table).addClass('dt-cards').find('tbody tr').each((i, tr) => {
			$(tr).children('td').each((col, td) => {
				const title = titles[col] ?? '';
				const isAction = title === '' || /^actions?$/i.test(title);
				td.setAttribute('data-label', isAction ? '' : title);
				td.classList.toggle('dt-card-actions', isAction);
			});
		});
	};

	// Card rows on phones for any DataTable (create() does this itself): for tables set up with
	// `new DataTable(...)` directly. The table must sit inside a .data-grid element.
	const enableCards = (tbl) => {
		$(tbl.table().node()).addClass('dt-cards');
		tbl.on('draw', () => labelCells(tbl));
		labelCells(tbl);
	};

	// Creates a grid that loads a JSON array from `url`. `options` are DataTables options
	// (columns, pageLength, language overrides, ...) merged over the defaults.
	// Pass `mobileCards: false` to keep the plain (horizontally scrolling) table on phones.
	const create = (selector, url, options) => {
		// Show load errors in the page's .grid-error box instead of DataTables' alert().
		DataTable.ext.errMode = 'none';

		const settings = $.extend(true, {}, defaults, options, {
			ajax: {
				url: url,
				dataSrc: '',
				headers: { 'RequestVerificationToken': antiForgeryToken() },
			},
		});

		const tbl = new DataTable(selector, settings);
		const errorBox = $(selector).closest('.data-grid').find('.grid-error');

		if (settings.mobileCards !== false) {
			enableCards(tbl);
		}

		tbl.on('error.dt', () => {
			errorBox.text('Could not load the list. Please reload the page or try again later.').removeClass('d-none');
		});

		tbl.on('xhr.dt', () => {
			errorBox.addClass('d-none');
		});

		return tbl;
	};

	// Exact (whole value) match on one column, or clears that column's filter.
	const filterExact = (tbl, index, value) => {
		const term = value ? '^' + DataTable.util.escapeRegex(value) + '$' : '';
		tbl.column(index).search(term, true, false).draw();
	};

	// Match one item in a comma-separated column such as "Admin, Permit Issuer".
	const filterListItem = (tbl, index, value) => {
		const term = value ? '(^|, )' + DataTable.util.escapeRegex(value) + '(,|$)' : '';
		tbl.column(index).search(term, true, false).draw();
	};

	// "Delete selected" for Super Users. Adds a bar above the grid (select all on this page + a Delete selected
	// button) and a checkbox in front of each row's first cell (the card title on phones), so the columns, their
	// indexes and the phone cards don't change. A dialog confirms, runs the delete and lists what was skipped.
	//
	// options:
	//   url      POST endpoint: { ids: [...] } -> { deleted, failed: [{ id, name, reason }] } (AjaxBulkDeleteResult)
	//   id(row)  the id sent for a row            label(row)  what the confirm list shows
	//   one/many the noun ("permit"/"permits")    note        extra line in the confirm dialog
	//   canSelect(row)  optional: false = no checkbox for that row
	//   afterDelete()   optional: runs after the grid has reloaded
	const bulkDelete = (tbl, options) => {
		const opts = Object.assign({ canSelect: () => true, note: '' }, options);
		const selected = new Map();                 // id -> label, kept across pages until the grid reloads
		const grid = $(tbl.table().node()).closest('.data-grid');
		const barId = 'bulkAll-' + Math.random().toString(36).slice(2);
		const bar = $(`
			<div class="bulk-bar d-flex flex-wrap align-items-center gap-3 mb-3">
				<div class="form-check mb-0"><input type="checkbox" class="form-check-input bulk-all" id="${barId}" />
					<label class="form-check-label small" for="${barId}">Select all on this page</label></div>
				<button type="button" class="btn btn-sm btn-outline-danger bulk-delete" disabled>
					<i class="fa-solid fa-trash-xmark me-1"></i>Delete selected <span class="badge text-bg-danger ms-1 bulk-count">0</span></button>
				<button type="button" class="btn btn-sm btn-outline-secondary bulk-clear" disabled title="Clear the selection on every page">
					<i class="fa-solid fa-xmark me-1"></i>Clear selection</button>
			</div>`);
		grid.find('.grid-error').after(bar);

		const rowsOnPage = () => tbl.rows({ page: 'current' });

		const syncBar = () => {
			bar.find('.bulk-count').text(selected.size);
			bar.find('.bulk-delete, .bulk-clear').prop('disabled', selected.size === 0);
			const boxes = $(tbl.table().node()).find('.row-select');
			const ticked = boxes.filter(':checked').length;
			bar.find('.bulk-all').prop('checked', boxes.length > 0 && ticked === boxes.length)
				.prop('indeterminate', ticked > 0 && ticked < boxes.length);
		};

		// Put the checkbox into each row on the page (DataTables reuses row nodes, so only once per node).
		const decorate = () => {
			rowsOnPage().every(function () {
				const row = this.data();
				const cell = this.node()?.cells[0];
				if (!cell || !opts.canSelect(row)) return;
				let box = cell.querySelector('.row-select');
				if (!box) {
					box = document.createElement('input');
					box.type = 'checkbox';
					box.className = 'form-check-input row-select';
					box.setAttribute('aria-label', 'Select ' + opts.label(row));
					cell.prepend(box);
				}
				box.dataset.id = opts.id(row);
				box.dataset.label = opts.label(row);
				box.checked = selected.has(box.dataset.id);
			});
			syncBar();
		};

		tbl.on('draw', decorate);
		tbl.on('xhr.dt', () => { selected.clear(); });   // a reload starts a fresh selection
		decorate();

		$(tbl.table().node()).on('change', '.row-select', function () {
			if (this.checked) selected.set(this.dataset.id, this.dataset.label);
			else selected.delete(this.dataset.id);
			syncBar();
		});

		bar.find('.bulk-all').on('change', function () {
			const on = this.checked;
			$(tbl.table().node()).find('.row-select').each(function () {
				this.checked = on;
				if (on) selected.set(this.dataset.id, this.dataset.label);
				else selected.delete(this.dataset.id);
			});
			syncBar();
		});

		// Forgets every ticked row, on all pages (not just the one showing).
		bar.find('.bulk-clear').on('click', () => {
			selected.clear();
			$(tbl.table().node()).find('.row-select').prop('checked', false);
			syncBar();
		});

		// ---- confirm / result dialog ----
		let modal = null;
		const dialog = () => {
			if (modal) return modal;
			const el = $(`
				<div class="modal fade" tabindex="-1" aria-hidden="true" data-bs-backdrop="static" data-bs-keyboard="false">
					<div class="modal-dialog modal-dialog-centered"><div class="modal-content">
						<div class="modal-header bg-warning">
							<h6 class="modal-title w-100 fw-semibold" data-title></h6>
							<button type="button" class="btn btn-clear" data-bs-dismiss="modal" aria-label="Close"><i class="fa-regular fa-close"></i></button>
						</div>
						<div class="modal-body"><div data-msg></div><ul class="small mt-2 mb-0 ps-3" data-list></ul></div>
						<div class="modal-footer">
							<button type="button" class="btn btn-sm btn-secondary" data-bs-dismiss="modal" data-cancel>Cancel</button>
							<button type="button" class="btn btn-sm btn-danger" data-go><span class="spinner-border spinner-border-sm me-1 d-none"></span>Yes, delete</button>
						</div>
					</div></div>
				</div>`).appendTo('body');
			modal = { el, bs: bootstrap.Modal.getOrCreateInstance(el[0]) };
			el.find('[data-go]').on('click', run);
			return modal;
		};

		const noun = (n) => n === 1 ? opts.one : opts.many;
		const setList = (items) => {
			const max = 8;
			const lis = items.slice(0, max).map(t => `<li>${escapeHtml(t)}</li>`);
			if (items.length > max) lis.push(`<li class="text-secondary">…and ${items.length - max} more</li>`);
			dialog().el.find('[data-list]').html(lis.join(''));
		};

		const confirm = () => {
			const m = dialog();
			const n = selected.size;
			m.el.find('[data-title]').text(`Delete ${n} ${noun(n)}`);
			m.el.find('[data-msg]').text(`You are about to delete ${n} ${noun(n)}.${opts.note ? ' ' + opts.note : ''} Are you sure?`);
			setList([...selected.values()]);
			m.el.find('[data-go]').removeClass('d-none').prop('disabled', false).find('.spinner-border').addClass('d-none');
			m.el.find('[data-cancel]').text('No');
			m.bs.show();
		};

		const showResult = (title, message, items) => {
			const m = dialog();
			m.el.find('[data-title]').text(title);
			m.el.find('[data-msg]').text(message);
			setList(items);
			m.el.find('[data-go]').addClass('d-none');
			m.el.find('[data-cancel]').text('Close');
		};

		async function run() {
			const m = dialog();
			const go = m.el.find('[data-go]').prop('disabled', true);
			go.find('.spinner-border').removeClass('d-none');
			m.el.find('[data-cancel]').prop('disabled', true);

			try {
				const res = await fetch(opts.url, {
					method: 'POST',
					headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': antiForgeryToken() },
					body: JSON.stringify({ ids: [...selected.keys()] }),
				});
				if (!res.ok) throw new Error(res.status === 403 ? 'You are not allowed to delete these records.' : 'The delete failed. Please try again.');

				const result = await res.json();
				const failed = result.failed ?? [];
				if (failed.length === 0) {
					showResult('Deleted', `${result.deleted} ${noun(result.deleted)} deleted.`, []);
				} else {
					showResult(result.deleted > 0 ? 'Partly deleted' : 'Nothing deleted',
						`${result.deleted} ${noun(result.deleted)} deleted. ${failed.length} could not be deleted:`,
						failed.map(f => `${f.name}: ${f.reason}`));
				}
				tbl.ajax.reload(() => { selected.clear(); decorate(); opts.afterDelete?.(); }, false);
			} catch (e) {
				showResult('Delete failed', e.message || 'The delete failed. Please try again.', []);
			} finally {
				m.el.find('[data-cancel]').prop('disabled', false);
			}
		}

		bar.find('.bulk-delete').on('click', confirm);
	};

	return {
		antiForgeryToken,
		bulkDelete,
		escapeHtml,
		renderText,
		renderDate,
		renderActive,
		htmlColumn,
		create,
		enableCards,
		labelTable,
		filterExact,
		filterListItem,
	};

})();
