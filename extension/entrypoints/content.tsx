import { defineContentScript, createShadowRootUi } from '#imports';
import ReactDOM from 'react-dom/client';
import { SelectionOverlay } from '@/components/SelectionOverlay';

export default defineContentScript({
  matches: ['<all_urls>'],
  // 'ui' lets WXT inject component CSS into the shadow root, isolated from the page.
  cssInjectionMode: 'ui',
  async main(ctx) {
    // Best-effort: load the brand fonts. Falls back to system fonts if a strict
    // page CSP blocks the request.
    try {
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href =
        'https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500;600&display=swap';
      document.head.appendChild(link);
    } catch {
      /* ignore */
    }

    const ui = await createShadowRootUi(ctx, {
      name: 'synapseai-overlay',
      position: 'overlay',
      anchor: 'body',
      onMount: (container) => {
        const root = ReactDOM.createRoot(container);
        root.render(<SelectionOverlay />);
        return root;
      },
      onRemove: (root) => root?.unmount(),
    });

    // The host element must not intercept page clicks; interactive children
    // re-enable pointer events themselves.
    ui.shadowHost.style.pointerEvents = 'none';
    ui.shadowHost.style.position = 'fixed';
    ui.shadowHost.style.zIndex = '2147483647';

    ui.mount();
  },
});
