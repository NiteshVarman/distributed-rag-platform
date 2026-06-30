# SynapseAI — RAG Assistant (Chrome Extension)

Hyper-modern AI overlay for any web page. Select text to summon a radial **orb**
that fans out AI tools (Summarize, Explain, Translate, Rewrite, Expand, Grammar),
plus an **Ask AI** chat panel whose answers are **grounded in the page via RAG**
when the page has been processed.

## Stack
- **WXT** (Vite-based extension framework) + **React 18** + **TypeScript**
- **motion** (Framer Motion) for animation, **lucide-react** for icons
- Content-script UI mounted in a **closed Shadow DOM** (isolated from host pages)
- Background **service worker** proxies API calls (avoids https→http mixed content)

## Architecture
```
content script (Shadow-DOM overlay)  ──sendMessage──▶  background worker  ──fetch──▶  /api/text-action
popup (process page / status / settings)  ──fetch──▶  /api/process-url, /api/job-status, /api/health
```
The backend lives in `../src/DistributedRag.Api`. The worker reads the API URL from
`chrome.storage.local` (`apiUrl`, default `http://localhost:5062`), set in the popup.

## Develop
```bash
pnpm install
pnpm dev        # launches a dev browser with HMR
```

## Build & load
```bash
pnpm build      # outputs .output/chrome-mv3
```
Then: `chrome://extensions` → Developer mode → **Load unpacked** → select
`extension/.output/chrome-mv3`.

> Reload order matters: after rebuilding, reload the extension **and refresh the
> web page** (content scripts only inject on page load).

## Demo flow
1. Start the API (`dotnet run` in `src/DistributedRag.Api`).
2. Open the popup, **Process** the current page (for grounded Ask AI).
3. On the page, select text → orb appears → click a tool, or **Ask AI** to chat.
   The chat badge shows **FROM THIS PAGE** (RAG) or **GENERAL**.

## Entry points
- `entrypoints/content.tsx` — mounts `SelectionOverlay` in a Shadow DOM
- `entrypoints/background.ts` — API proxy
- `entrypoints/popup/` — process / status / settings UI
- `components/` — `SelectionOverlay`, `RadialSystem`, `ResultCard`, `TranslatePicker`, `ChatPanel`, `Popup`
- `lib/` — `api.ts` (worker bridge), `tools.tsx` (tool config), `types.ts`
