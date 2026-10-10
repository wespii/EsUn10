// Runs before styles load to avoid a bright flash when a dark preference is saved.
(() => {
    const key = 'esun10.theme';
    const system = window.matchMedia('(prefers-color-scheme: dark)');
    let preference = null;
    try { const saved = localStorage.getItem(key); if (saved === 'dark' || saved === 'light') preference = saved; } catch {}
    function apply(theme) {
        const dark = theme === 'dark';
        document.documentElement.dataset.theme = theme;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? '#101b19' : '#183f38');
        const button = document.querySelector('#themeToggle');
        if (!button) return;
        button.setAttribute('aria-pressed', String(dark));
        button.title = dark ? 'Activar modo claro' : 'Activar modo oscuro';
        button.querySelector('.theme-icon').textContent = dark ? '☀' : '☾';
        button.querySelector('.theme-label').textContent = dark ? 'Modo claro' : 'Modo oscuro';
    }
    const current = () => preference || (system.matches ? 'dark' : 'light');
    window.toggleTheme = () => {
        preference = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem(key, preference); } catch {}
        apply(preference);
    };
    apply(current());
    document.addEventListener('DOMContentLoaded', () => apply(current()), { once: true });
    system.addEventListener('change', () => { if (!preference) apply(current()); });
    window.addEventListener('storage', event => {
        if (event.key !== key && event.key !== null) return;
        preference = event.newValue === 'dark' || event.newValue === 'light' ? event.newValue : null;
        apply(current());
    });
})();
