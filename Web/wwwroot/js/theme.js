// Light / dark / system theme. Loaded synchronously in <head> so the theme is applied before
// the first paint. The choice is kept per browser in localStorage ('system' when unset).
(function () {
	const KEY = 'pp-theme';
	const media = window.matchMedia('(prefers-color-scheme: dark)');

	function stored() {
		try {
			const v = localStorage.getItem(KEY);
			return v === 'light' || v === 'dark' ? v : 'system';
		} catch {
			return 'system';
		}
	}

	function resolve(pref) {
		return pref === 'system' ? (media.matches ? 'dark' : 'light') : pref;
	}

	function apply(pref) {
		const theme = resolve(pref);
		const root = document.documentElement;
		const changed = root.getAttribute('data-bs-theme') !== theme;

		root.setAttribute('data-bs-theme', theme);
		root.style.colorScheme = theme;
		// webawesome.css (loaded in the layouts) colours native elements such as <label> from its own theme.
		root.classList.toggle('wa-dark', theme === 'dark');
		root.classList.toggle('wa-light', theme !== 'dark');
		syncToggles(pref, theme);

		if (changed) {
			syncCharts();
			window.dispatchEvent(new CustomEvent('pp:themechange', { detail: { theme, preference: pref } }));
		}
	}

	// A theme colour (CSS custom property from site.css), e.g. color('--text').
	function color(name) {
		return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
	}

	// Chart.js draws on a canvas, so CSS can't restyle it: point its default text and
	// gridline colours at the theme and redraw any charts already on the page.
	function syncCharts() {
		if (!window.Chart) return;
		Chart.defaults.color = color('--muted');
		Chart.defaults.borderColor = color('--border');
		Object.values(Chart.instances || {}).forEach(c => c.update('none'));
	}

	function syncToggles(pref, theme) {
		const icon = theme === 'dark' ? 'fa-moon' : 'fa-sun';

		document.querySelectorAll('[data-theme-value]').forEach(el => {
			const active = el.getAttribute('data-theme-value') === pref;
			el.classList.toggle('active', active);
			el.setAttribute('aria-pressed', active);
		});

		document.querySelectorAll('[data-theme-icon]').forEach(el => {
			el.classList.remove('fa-sun', 'fa-moon');
			el.classList.add(icon);
		});
	}

	function set(pref) {
		try {
			if (pref === 'system') localStorage.removeItem(KEY);
			else localStorage.setItem(KEY, pref);
		} catch { /* storage blocked: still switch for this page */ }

		apply(pref);
	}

	apply(stored());

	media.addEventListener('change', () => {
		if (stored() === 'system') apply('system');
	});

	// Another tab changed the theme.
	window.addEventListener('storage', e => {
		if (e.key === KEY || e.key === null) apply(stored());
	});

	document.addEventListener('click', e => {
		const btn = e.target.closest('[data-theme-value]');
		if (!btn) return;
		e.preventDefault();
		set(btn.getAttribute('data-theme-value'));
	});

	document.addEventListener('DOMContentLoaded', () => {
		syncToggles(stored(), resolve(stored()));
		syncCharts();
	});

	window.PPTheme = {
		get: () => resolve(stored()),
		preference: stored,
		set,
		isDark: () => resolve(stored()) === 'dark',
		color,
	};
})();
