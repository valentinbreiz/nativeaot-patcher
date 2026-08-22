// Swap the diagram SVGs with their baked dark variants when the site theme is
// dark. The SVGs also carry a prefers-color-scheme media query for renderers
// we don't control (github.com), but browsers that pin the media query to
// light (LibreWolf's resistFingerprinting, older engines) would otherwise
// always show the light variant; keying on data-bs-theme follows the site
// toggle everywhere.
const diagramSrc = /^(.*\/images\/diagrams\/(?:gc|sched)-[a-z-]+?)(-dark)?\.svg$/;

function applyDiagramTheme() {
    const dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
    for (const img of document.querySelectorAll('img[src*="images/diagrams/"]')) {
        const m = img.src.match(diagramSrc);
        if (!m) {
            continue;
        }
        const src = m[1] + (dark ? '-dark' : '') + '.svg';
        if (img.src !== src) {
            img.src = src;
        }
    }
}

// Version selector. Released docs are frozen by release.yml into vX.Y.Z/
// subfolders next to the dev site, plus a latest/ alias, described by a
// versions.json at the outer root. The dropdown only appears once that file
// exists, i.e. after the first tagged release. The site root is taken from
// the docfx:rel meta; when its last path segment is a version folder, the
// outer root (where versions.json and the sibling versions live) is one
// level up.
const versionFolder = /^(v\d[^/]*|latest)$/;

async function initVersionSelector() {
    const rel = document.querySelector('meta[name="docfx:rel"]')?.content ?? '';
    const siteRoot = new URL(rel || './', location.href);
    const segments = siteRoot.pathname.split('/').filter(s => s !== '');
    const folder = segments.length > 0 ? segments[segments.length - 1] : '';
    const inVersion = versionFolder.test(folder);
    const outerRoot = inVersion ? new URL('../', siteRoot) : siteRoot;

    let manifest;
    try {
        const res = await fetch(new URL('versions.json', outerRoot));
        if (!res.ok) {
            return;
        }
        manifest = await res.json();
    } catch {
        return;
    }
    if (!Array.isArray(manifest.versions) || manifest.versions.length === 0) {
        return;
    }

    const current = !inVersion ? 'dev'
        : folder === 'latest' ? manifest.latest
        : folder;
    const select = document.createElement('select');
    select.className = 'form-select form-select-sm';
    select.setAttribute('aria-label', 'Documentation version');
    for (const name of ['dev', ...manifest.versions]) {
        const option = document.createElement('option');
        option.value = name === 'dev' ? './' : name + '/';
        option.textContent = name === manifest.latest ? name + ' (latest)' : name;
        if (name === current) {
            option.setAttribute('selected', '');
        }
        select.append(option);
    }

    // Keep the reader on the same page when it exists in the target version,
    // fall back to that version's landing page when it does not.
    const pagePath = location.pathname.slice(siteRoot.pathname.length);
    select.addEventListener('change', async () => {
        const targetRoot = new URL(select.value, outerRoot);
        const samePage = new URL(pagePath, targetRoot);
        try {
            const probe = await fetch(samePage, { method: 'HEAD' });
            location.href = probe.ok ? samePage : targetRoot;
        } catch {
            location.href = targetRoot;
        }
    });

    const container = document.createElement('div');
    container.className = 'version-select';
    container.append(select);
    document.getElementById('navbar')?.after(container);
}

export default {
    start: () => {
        new MutationObserver(applyDiagramTheme)
            .observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
        applyDiagramTheme();
        initVersionSelector();
    }
}
