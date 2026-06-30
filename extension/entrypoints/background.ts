import { defineBackground } from '#imports';

const DEFAULT_API_URL = 'https://api-distributed-rag.azurewebsites.net';
const REQUEST_TIMEOUT_MS = 30000;

// Proxies content-script API calls. The worker runs in the chrome-extension://
// origin (a secure context where http://localhost is allowed), so an https page
// can reach the local API without mixed-content blocking. Keep it stateless —
// the worker is ephemeral and respawns per event.
export default defineBackground(() => {
  chrome.runtime.onMessage.addListener((message: any, _sender, sendResponse) => {
    const handler = ROUTES[message?.type];
    if (!handler) return false;
    handler(message)
      .then((r) => sendResponse({ ok: true, ...r }))
      .catch((e) => sendResponse({ ok: false, error: e?.message || 'Request failed' }));
    return true; // keep the channel open for the async sendResponse
  });
});

const ROUTES: Record<string, (m: any) => Promise<any>> = {
  TEXT_ACTION: (m) =>
    apiPost('/api/text-action', {
      action: m.action,
      text: m.text,
      targetLanguage: m.targetLanguage || null,
      pageUrl: m.pageUrl || null,
    }),
  PROCESS_URL: (m) => apiPost('/api/process-url', { url: m.url }),
  JOB_STATUS: (m) => apiGet(`/api/job-status/${encodeURIComponent(m.taskId)}`),
  PAGE_STATUS: (m) => apiGet(`/api/page-status?url=${encodeURIComponent(m.url)}`),
};

async function apiPost(path: string, body: unknown) {
  const apiUrl = await getApiUrl();
  const res = await fetch(`${apiUrl}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
  return readJson(res);
}

async function apiGet(path: string) {
  const apiUrl = await getApiUrl();
  const res = await fetch(`${apiUrl}${path}`, { signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) });
  return readJson(res);
}

async function readJson(res: Response) {
  if (!res.ok) {
    const err = await res.json().catch(() => ({}) as any);
    throw new Error(err.error || err.detail || `Server error (HTTP ${res.status})`);
  }
  return res.json();
}

async function getApiUrl(): Promise<string> {
  try {
    const { apiUrl } = await chrome.storage.local.get('apiUrl');
    return (apiUrl || DEFAULT_API_URL).replace(/\/+$/, '');
  } catch {
    return DEFAULT_API_URL;
  }
}
