/*
 * "Locations" picker on the user New/Edit pages (Treeselect.js, replacing the Kendo DropDownTree).
 * Load after Treeselect (see Views/Shared/_LocationPickerScripts.cshtml).
 *
 * The selection is written to a hidden input as a JSON array of location ids, the same shape the
 * Kendo version saved: every ticked location, where a parent counts as ticked only when all of its
 * children are ticked. Ticking a parent ticks all of its children.
 */
window.LocationPicker = (() => {

	// Treeselect reports only the ticked leaves; add back each parent whose children are all ticked.
	const withTickedParents = (nodes, leafValues) => {
		const ticked = new Set(leafValues.map(String));
		const result = [];

		const walk = (node) => {
			if (!node.children || node.children.length === 0) {
				if (ticked.has(node.value)) {
					result.push(node.value);
					return true;
				}
				return false;
			}

			let allTicked = true;
			for (const child of node.children) {
				if (!walk(child)) allTicked = false; // keep walking: other children may be ticked
			}

			if (allTicked) result.push(node.value);
			return allTicked;
		};

		nodes.forEach(walk);
		return result;
	};

	// Parses the hidden input's JSON array; tolerates an empty or invalid value.
	const readIds = (input) => {
		try {
			const ids = JSON.parse(input.value || '[]');
			return Array.isArray(ids) ? ids.map(id => String(id).toLowerCase()) : [];
		} catch {
			return [];
		}
	};

	// container: element to render into; input: hidden input holding the JSON id array;
	// url: endpoint returning [{ name, value, children }].
	const create = async (container, input, url) => {
		let options;

		try {
			const response = await fetch(url, { headers: { 'Accept': 'application/json' } });
			if (!response.ok) throw new Error(response.statusText);
			options = await response.json();
		} catch {
			container.innerHTML = '<div class="form-control text-danger">Could not load locations. Please reload the page.</div>';
			return null;
		}

		return new Treeselect({
			parentHtmlContainer: container,
			options: options,
			value: readIds(input),
			placeholder: '(select locations)',
			emptyText: 'No locations found.',
			expandSelected: true,
			inputCallback: (value) => {
				input.value = JSON.stringify(withTickedParents(options, value));
			},
		});
	};

	return { create };

})();
