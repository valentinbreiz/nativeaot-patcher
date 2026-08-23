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

export default {
    start: () => {
        new MutationObserver(applyDiagramTheme)
            .observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
        applyDiagramTheme();
    }
}
