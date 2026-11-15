window.leaveApp = {
    downloadBase64: function (filename, base64, contentType) {
        const raw = atob(base64);
        const bytes = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        const blob = new Blob([bytes], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1500);
    }
};

window.leaveApp.submitLogout = () => document.getElementById("logout-form").requestSubmit();

window.leaveApp.scrollToId = (id) => {
    setTimeout(() => {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: "smooth", block: "center" });
    }, 50);
};


// Thanh bên: thu gọn/mở rộng (nhớ qua localStorage) + tooltip khi chỉ còn icon
(function () {
    const KEY = 'qlnp.navCollapsed';
    const root = document.documentElement;
    let tip = null;

    function stored() {
        try { return localStorage.getItem(KEY) === '1'; } catch { return false; }
    }
    function apply() { root.classList.toggle('nav-collapsed', stored()); }
    function hideTip() { if (tip) tip.classList.remove('show'); }

    window.leaveApp.toggleNav = function () {
        const collapsed = root.classList.toggle('nav-collapsed');
        try { localStorage.setItem(KEY, collapsed ? '1' : '0'); } catch { }
        hideTip();
    };

    document.addEventListener('mouseover', function (e) {
        if (!root.classList.contains('nav-collapsed')) return;
        const item = e.target.closest && e.target.closest('.app-sidebar .sb-link, .app-sidebar .sb-meta, .app-sidebar .sb-user, .app-sidebar .sb-brand');
        if (!item) { hideTip(); return; }
        const label = item.getAttribute('data-tip') || (item.querySelector('.nav-label') || {}).textContent || '';
        if (!label.trim()) { hideTip(); return; }
        if (!tip) {
            tip = document.createElement('div');
            tip.className = 'sb-tooltip';
            tip.setAttribute('role', 'tooltip');
            document.body.appendChild(tip);
        }
        const r = item.getBoundingClientRect();
        tip.textContent = label.trim().replace(/\s+/g, ' ');
        tip.style.top = (r.top + r.height / 2) + 'px';
        tip.style.left = (r.right + 10) + 'px';
        tip.classList.add('show');
    });
    document.addEventListener('scroll', hideTip, true);
    document.addEventListener('click', hideTip, true);

    apply();
    if (window.Blazor && Blazor.addEventListener) Blazor.addEventListener('enhancedload', apply);
})();
