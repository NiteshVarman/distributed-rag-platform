import { useState, useEffect, useRef, useCallback } from 'react';
import { Zap, Sparkles, ArrowRight, Loader2, CheckCircle2, AlertTriangle } from 'lucide-react';

const DEFAULT_API_URL = 'https://api-distributed-rag.azurewebsites.net';

type Conn = 'checking' | 'online' | 'offline';

interface Status {
  status: string;
  progress: number;
  currentStep: string;
  error?: string;
}

export function Popup() {
  const [apiUrl, setApiUrl] = useState(DEFAULT_API_URL);
  const [url, setUrl] = useState('');
  const [conn, setConn] = useState<Conn>('checking');
  const [taskId, setTaskId] = useState<string | null>(null);
  const [status, setStatus] = useState<Status | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // ─── Init: load settings, current tab URL, health ───
  useEffect(() => {
    (async () => {
      try {
        const s = await chrome.storage.local.get(['apiUrl', 'lastUrl']);
        if (s.apiUrl) setApiUrl(s.apiUrl);
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        setUrl(tab?.url || s.lastUrl || '');
      } catch {
        /* ignore */
      }
    })();
  }, []);

  const checkHealth = useCallback(async (base: string) => {
    setConn('checking');
    try {
      const res = await fetch(`${base}/api/health`, { signal: AbortSignal.timeout(4000) });
      setConn(res.ok ? 'online' : 'offline');
    } catch {
      setConn('offline');
    }
  }, []);

  useEffect(() => {
    void checkHealth(apiUrl);
  }, [apiUrl, checkHealth]);

  const stopPoll = () => {
    if (pollRef.current) {
      clearInterval(pollRef.current);
      pollRef.current = null;
    }
  };

  const poll = useCallback(
    async (id: string) => {
      try {
        const res = await fetch(`${apiUrl}/api/job-status/${id}`);
        if (!res.ok) return;
        const data = await res.json();
        setStatus({ status: data.status, progress: data.progress, currentStep: data.currentStep, error: data.error });
        if (data.status === 'COMPLETED') {
          stopPoll();
          setBusy(false);
        } else if (data.status === 'FAILED') {
          stopPoll();
          setBusy(false);
          setErr(data.error || 'Processing failed');
        }
      } catch {
        /* keep polling */
      }
    },
    [apiUrl],
  );

  useEffect(() => () => stopPoll(), []);

  const process = useCallback(async () => {
    const target = url.trim();
    if (!target) return;
    try {
      new URL(target);
    } catch {
      setErr('Enter a valid URL');
      return;
    }
    setErr(null);
    setBusy(true);
    setStatus({ status: 'QUEUED', progress: 0, currentStep: 'Queued…' });
    try {
      const res = await fetch(`${apiUrl}/api/process-url`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ url: target }),
      });
      if (!res.ok) {
        const e = await res.json().catch(() => ({}));
        throw new Error(e.error || e.detail || `HTTP ${res.status}`);
      }
      const data = await res.json();
      setTaskId(data.taskId);
      await chrome.storage.local.set({ lastTaskId: data.taskId, lastUrl: target });
      stopPoll();
      pollRef.current = setInterval(() => poll(data.taskId), 2000);
      void poll(data.taskId);
    } catch (e: any) {
      setErr(e?.message || 'Failed to queue URL');
      setBusy(false);
    }
  }, [url, apiUrl, poll]);

  const saveApi = useCallback(async () => {
    const clean = apiUrl.replace(/\/+$/, '');
    setApiUrl(clean);
    await chrome.storage.local.set({ apiUrl: clean });
    void checkHealth(clean);
  }, [apiUrl, checkHealth]);

  const connColor = conn === 'online' ? '#00d4ff' : conn === 'offline' ? '#fb7185' : '#fbbf24';
  const done = status?.status === 'COMPLETED';

  return (
    <div style={{ fontFamily: "'Inter',sans-serif", color: '#eeeeff', padding: 0 }}>
      {/* Header */}
      <div
        style={{
          position: 'relative',
          padding: '16px 18px',
          borderBottom: '1px solid rgba(255,255,255,0.06)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <div style={{ position: 'absolute', top: 0, left: 0, right: 0, height: 1, background: 'linear-gradient(90deg, transparent, #7c5af5, #00d4ff, transparent)' }} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ width: 30, height: 30, borderRadius: 9, background: 'linear-gradient(135deg,#7c5af5,#00d4ff)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Zap size={15} color="white" />
          </div>
          <div>
            <p style={{ fontSize: 14, fontWeight: 600, lineHeight: 1 }}>SynapseAI</p>
            <p style={{ fontSize: 10, color: '#7777aa', marginTop: 3, fontFamily: "'JetBrains Mono',monospace" }}>RAG Assistant</p>
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <div style={{ width: 7, height: 7, borderRadius: '50%', background: connColor, boxShadow: `0 0 8px ${connColor}`, animation: 'nx-breathe 2.5s ease-in-out infinite' }} />
          <span style={{ fontSize: 10, color: '#55557a', fontFamily: "'JetBrains Mono',monospace" }}>{conn}</span>
        </div>
      </div>

      <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 14 }}>
        {/* Process URL */}
        <div>
          <label style={{ fontSize: 10, color: '#7777aa', fontFamily: "'JetBrains Mono',monospace", letterSpacing: '0.08em', textTransform: 'uppercase' }}>
            Process this page
          </label>
          <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            <input
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://…"
              style={{
                flex: 1,
                padding: '10px 12px',
                borderRadius: 10,
                background: 'rgba(22,22,40,0.9)',
                border: '1px solid rgba(255,255,255,0.08)',
                color: '#eeeeff',
                fontSize: 12,
                outline: 'none',
              }}
            />
            <button
              onClick={process}
              disabled={busy}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 6,
                padding: '0 14px',
                borderRadius: 10,
                background: busy ? 'rgba(124,90,245,0.4)' : 'linear-gradient(135deg,#7c5af5,#3b82f6)',
                border: 'none',
                color: 'white',
                fontSize: 12,
                fontWeight: 600,
                cursor: busy ? 'default' : 'pointer',
              }}
            >
              {busy ? <Loader2 size={14} style={{ animation: 'nx-spin 0.9s linear infinite' }} /> : <ArrowRight size={14} />}
              {busy ? 'Working' : 'Process'}
            </button>
          </div>
        </div>

        {/* Status */}
        {status && (
          <div style={{ padding: '12px 14px', borderRadius: 12, background: 'rgba(124,90,245,0.06)', border: '1px solid rgba(124,90,245,0.18)' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
              {done ? <CheckCircle2 size={15} color="#34d399" /> : <Loader2 size={15} color="#a78bfa" style={{ animation: 'nx-spin 0.9s linear infinite' }} />}
              <span style={{ fontSize: 12, color: done ? '#34d399' : '#a78bfa', fontWeight: 600 }}>{status.status}</span>
              <span style={{ marginLeft: 'auto', fontSize: 11, color: '#7777aa', fontFamily: "'JetBrains Mono',monospace" }}>{status.progress}%</span>
            </div>
            <div style={{ height: 5, borderRadius: 4, background: 'rgba(255,255,255,0.08)', overflow: 'hidden' }}>
              <div style={{ height: '100%', width: `${status.progress}%`, background: 'linear-gradient(90deg,#7c5af5,#00d4ff)', transition: 'width 0.4s ease' }} />
            </div>
            <p style={{ fontSize: 11, color: '#9999bb', marginTop: 8 }}>{status.currentStep}</p>
          </div>
        )}

        {/* Error */}
        {err && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 12px', borderRadius: 10, background: 'rgba(251,113,133,0.1)', border: '1px solid rgba(251,113,133,0.25)' }}>
            <AlertTriangle size={14} color="#fb7185" />
            <span style={{ fontSize: 11, color: '#fda4af' }}>{err}</span>
          </div>
        )}

        {/* Hint */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 9, padding: '11px 13px', borderRadius: 12, background: 'rgba(0,212,255,0.06)', border: '1px solid rgba(0,212,255,0.18)' }}>
          <Sparkles size={14} color="#00d4ff" style={{ flexShrink: 0 }} />
          <p style={{ fontSize: 11, color: '#8aa' }}>
            <span style={{ color: '#00d4ff', fontWeight: 500 }}>Select any text</span> on a page to summon the AI orb, or use{' '}
            <span style={{ color: '#a78bfa', fontWeight: 500 }}>Ask&nbsp;AI</span> for page-grounded answers.
          </p>
        </div>

        {/* Settings */}
        <details style={{ fontSize: 11 }}>
          <summary style={{ cursor: 'pointer', color: '#7777aa', fontFamily: "'JetBrains Mono',monospace", letterSpacing: '0.06em' }}>API settings</summary>
          <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
            <input
              value={apiUrl}
              onChange={(e) => setApiUrl(e.target.value)}
              style={{ flex: 1, padding: '8px 10px', borderRadius: 8, background: 'rgba(22,22,40,0.9)', border: '1px solid rgba(255,255,255,0.08)', color: '#eeeeff', fontSize: 11, outline: 'none' }}
            />
            <button onClick={saveApi} style={{ padding: '0 12px', borderRadius: 8, background: 'rgba(255,255,255,0.08)', border: 'none', color: '#cbd5e1', fontSize: 11, cursor: 'pointer' }}>
              Save
            </button>
          </div>
        </details>
      </div>
    </div>
  );
}
