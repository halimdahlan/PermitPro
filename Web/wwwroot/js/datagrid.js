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

	return {
		antiForgeryToken,
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
