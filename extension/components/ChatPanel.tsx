import { useState, useRef, useCallback, useEffect } from 'react';
import { motion } from 'motion/react';
import { Sparkles, X, Send, Loader2, CheckCircle2, AlertTriangle } from 'lucide-react';
import { runTextAction, getPageStatus, processPage, getJobStatus } from '@/lib/api';

type IndexState =
  | { phase: 'checking' }
  | { phase: 'indexing'; progress: number; step?: string }
  | { phase: 'ready' }
  | { phase: 'failed'; error?: string };

interface Msg {
  role: 'user' | 'ai';
  text: string;
  mode?: 'rag' | 'general' | 'n-a';
}

function LoadingDots() {
  return (
    <span style={{ display: 'inline-flex', gap: 3, alignItems: 'center' }}>
      {[0, 1, 2].map((i) => (
        <span
          key={i}
          style={{
            width: 6,
            height: 6,
            borderRadius: '50%',
            background: '#555578',
            animation: `nx-bounce 0.9s ease-in-out ${i * 0.15}s infinite`,
            display: 'inline-block',
          }}
        />
      ))}
    </span>
  );
}

function IndexBanner({ index }: { index: IndexState }) {
  const base: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 8,
    padding: '8px 14px',
    fontSize: 11,
    fontFamily: "'JetBrains Mono',monospace",
    borderBottom: '1px solid rgba(255,255,255,0.06)',
  };

  if (index.phase === 'ready') {
    return (
      <div style={{ ...base, color: '#34d399', background: 'rgba(52,211,153,0.08)' }}>
        <CheckCircle2 size={13} /> Grounded in this page
      </div>
    );
  }
  if (index.phase === 'failed') {
    return (
      <div style={{ ...base, color: '#fda4af', background: 'rgba(251,113,133,0.08)' }}>
        <AlertTriangle size={13} /> Couldn’t index this page — answers will be general
      </div>
    );
  }
  const progress = index.phase === 'indexing' ? index.progress : 0;
  const label = index.phase === 'checking' ? 'Checking page…' : `Indexing this page… ${progress}%`;
  return (
    <div style={{ ...base, flexDirection: 'column', alignItems: 'stretch', gap: 6, color: '#a78bfa', background: 'rgba(124,90,245,0.08)' }}>
      <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <Loader2 size={13} style={{ animation: 'nx-spin 0.9s linear infinite' }} /> {label}
      </span>
      <span style={{ height: 4, borderRadius: 3, background: 'rgba(255,255,255,0.08)', overflow: 'hidden' }}>
        <span style={{ display: 'block', height: '100%', width: `${progress}%`, background: 'linear-gradient(90deg,#7c5af5,#00d4ff)', transition: 'width 0.4s ease' }} />
      </span>
    </div>
  );
}

export function ChatPanel({ onClose }: { onClose: () => void }) {
  const [msgs, setMsgs] = useState<Msg[]>([
    { role: 'ai', text: "Hi! I'm indexing this page so I can answer from its content. You can start asking right away — answers become page-grounded once indexing finishes." },
  ]);
  const [input, setInput] = useState('');
  const [typing, setTyping] = useState(false);
  const [index, setIndex] = useState<IndexState>({ phase: 'checking' });
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [msgs, typing]);

  // Auto-index the current page on open: check status, start processing if needed, then poll.
  useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setInterval> | null = null;
    const stop = () => {
      if (timer) {
        clearInterval(timer);
        timer = null;
      }
    };

    const poll = (taskId: string) => {
      timer = setInterval(async () => {
        try {
          const js = await getJobStatus(taskId);
          if (cancelled) return;
          if (js.status === 'COMPLETED') {
            stop();
            setIndex({ phase: 'ready' });
          } else if (js.status === 'FAILED') {
            stop();
            setIndex({ phase: 'failed', error: js.error });
          } else {
            setIndex({ phase: 'indexing', progress: js.progress, step: js.currentStep });
          }
        } catch {
          /* keep polling */
        }
      }, 2000);
    };

    (async () => {
      try {
        const s = await getPageStatus(location.href);
        if (cancelled) return;
        if (s.processed) {
          setIndex({ phase: 'ready' });
          return;
        }
        let taskId = s.taskId;
        if (!s.exists || s.status === 'FAILED') {
          const p = await processPage(location.href);
          if (cancelled) return;
          taskId = p.taskId;
        }
        setIndex({ phase: 'indexing', progress: s.progress || 0, step: s.currentStep });
        if (taskId) poll(taskId);
      } catch (e: any) {
        if (!cancelled) setIndex({ phase: 'failed', error: e?.message });
      }
    })();

    return () => {
      cancelled = true;
      stop();
    };
  }, []);

  const send = useCallback(
    async (override?: string) => {
      const text = (override ?? input).trim();
      if (!text || typing) return;
      setMsgs((m) => [...m, { role: 'user', text }]);
      setInput('');
      setTyping(true);
      try {
        const { result, mode } = await runTextAction('answer', text, { pageUrl: location.href });
        setMsgs((m) => [...m, { role: 'ai', text: result, mode }]);
      } catch (err: any) {
        setMsgs((m) => [...m, { role: 'ai', text: err?.message || 'Something went wrong.', mode: 'n-a' }]);
      } finally {
        setTyping(false);
      }
    },
    [input, typing],
  );

  const chips = ["What's the main point?", 'Summarize this page', 'Key takeaways?'];

  return (
    <motion.div
      initial={{ x: '100%', opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ x: '100%', opacity: 0 }}
      transition={{ type: 'spring', stiffness: 320, damping: 34 }}
      onMouseDown={(e) => e.stopPropagation()}
      style={{
        position: 'fixed',
        right: 0,
        top: 0,
        bottom: 0,
        width: 360,
        maxWidth: '100vw',
        zIndex: 2147483647,
        pointerEvents: 'auto',
        background: 'rgba(7,7,18,0.98)',
        borderLeft: '1px solid rgba(124,90,245,0.2)',
        display: 'flex',
        flexDirection: 'column',
        backdropFilter: 'blur(24px)',
        boxShadow: '-12px 0 48px rgba(0,0,0,0.5), -1px 0 0 rgba(124,90,245,0.15)',
      }}
    >
      {/* Header */}
      <div
        style={{
          position: 'relative',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '16px 18px',
          borderBottom: '1px solid rgba(255,255,255,0.06)',
        }}
      >
        <div style={{ position: 'absolute', top: 0, left: 0, right: 0, height: 1, background: 'linear-gradient(90deg, transparent, #7c5af5, #00d4ff, transparent)' }} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div
            style={{
              position: 'relative',
              width: 32,
              height: 32,
              borderRadius: 10,
              background: 'rgba(124,90,245,0.18)',
              border: '1px solid rgba(124,90,245,0.3)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <Sparkles size={15} color="#a78bfa" />
            <div style={{ position: 'absolute', top: -3, right: -3, width: 8, height: 8, borderRadius: '50%', background: '#00d4ff', border: '2px solid #07070f' }} />
          </div>
          <div>
            <p style={{ fontSize: 14, fontWeight: 600, color: '#eeeeff', margin: 0, lineHeight: 1 }}>Ask AI</p>
            <p style={{ fontSize: 11, color: '#7777aa', margin: '3px 0 0', fontFamily: "'JetBrains Mono',monospace" }}>About this page</p>
          </div>
        </div>
        <button onClick={onClose} style={{ padding: '6px', borderRadius: 8, background: 'rgba(255,255,255,0.06)', border: 'none', cursor: 'pointer', color: '#777799', display: 'flex' }}>
          <X size={15} />
        </button>
      </div>

      {/* Indexing banner */}
      <IndexBanner index={index} />

      {/* Messages */}
      <div style={{ flex: 1, overflowY: 'auto', padding: '14px 14px 8px', display: 'flex', flexDirection: 'column', gap: 12, scrollbarWidth: 'none' }}>
        {msgs.map((m, i) => (
          <div key={i} style={{ display: 'flex', flexDirection: 'column', alignItems: m.role === 'user' ? 'flex-end' : 'flex-start' }}>
            <div style={{ display: 'flex', justifyContent: m.role === 'user' ? 'flex-end' : 'flex-start', gap: 8, width: '100%' }}>
              {m.role === 'ai' && (
                <div style={{ width: 26, height: 26, borderRadius: 8, background: 'rgba(124,90,245,0.18)', border: '1px solid rgba(124,90,245,0.3)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, marginTop: 2 }}>
                  <Sparkles size={12} color="#a78bfa" />
                </div>
              )}
              <div
                style={{
                  maxWidth: '80%',
                  padding: '10px 13px',
                  borderRadius: m.role === 'user' ? '14px 14px 4px 14px' : '14px 14px 14px 4px',
                  background: m.role === 'user' ? 'rgba(124,90,245,0.18)' : 'rgba(22,22,40,0.9)',
                  border: m.role === 'user' ? '1px solid rgba(124,90,245,0.22)' : '1px solid rgba(255,255,255,0.06)',
                  fontSize: 13,
                  lineHeight: 1.75,
                  color: 'rgba(238,238,255,0.88)',
                  fontFamily: "'Inter',sans-serif",
                  whiteSpace: 'pre-wrap',
                }}
              >
                {m.text}
              </div>
            </div>
            {m.role === 'ai' && (m.mode === 'rag' || m.mode === 'general') && (
              <span
                style={{
                  marginLeft: 34,
                  marginTop: 4,
                  fontSize: 9,
                  fontFamily: "'JetBrains Mono',monospace",
                  letterSpacing: '0.05em',
                  padding: '2px 7px',
                  borderRadius: 999,
                  color: m.mode === 'rag' ? '#34d399' : '#9aa0b4',
                  background: m.mode === 'rag' ? 'rgba(52,211,153,0.14)' : 'rgba(148,163,184,0.14)',
                }}
              >
                {m.mode === 'rag' ? '✓ FROM THIS PAGE' : 'GENERAL'}
              </span>
            )}
          </div>
        ))}
        {typing && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <div style={{ width: 26, height: 26, borderRadius: 8, background: 'rgba(124,90,245,0.18)', border: '1px solid rgba(124,90,245,0.3)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
              <Sparkles size={12} color="#a78bfa" />
            </div>
            <div style={{ padding: '10px 13px', borderRadius: '14px 14px 14px 4px', background: 'rgba(22,22,40,0.9)', border: '1px solid rgba(255,255,255,0.06)' }}>
              <LoadingDots />
            </div>
          </div>
        )}
        <div ref={endRef} />
      </div>

      {/* Quick chips */}
      {msgs.length <= 1 && (
        <div style={{ padding: '0 14px 10px', display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {chips.map((c) => (
            <button
              key={c}
              onClick={() => send(c)}
              style={{
                fontSize: 11,
                padding: '5px 11px',
                borderRadius: 20,
                background: 'rgba(255,255,255,0.05)',
                border: '1px solid rgba(255,255,255,0.1)',
                color: '#9999bb',
                cursor: 'pointer',
                fontFamily: "'Inter',sans-serif",
                transition: 'all 0.12s ease',
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.borderColor = 'rgba(124,90,245,0.4)';
                e.currentTarget.style.color = '#a78bfa';
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)';
                e.currentTarget.style.color = '#9999bb';
              }}
            >
              {c}
            </button>
          ))}
        </div>
      )}

      {/* Input */}
      <div style={{ padding: '12px 14px', borderTop: '1px solid rgba(255,255,255,0.06)' }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            padding: '10px 12px',
            borderRadius: 12,
            background: 'rgba(22,22,40,0.9)',
            border: '1px solid rgba(255,255,255,0.08)',
          }}
        >
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                send();
              }
            }}
            placeholder="Ask about this page…"
            autoFocus
            style={{ flex: 1, background: 'none', border: 'none', outline: 'none', fontSize: 13, color: '#eeeeff', fontFamily: "'Inter',sans-serif" }}
          />
          <button
            onClick={() => send()}
            disabled={!input.trim() || typing}
            style={{
              width: 30,
              height: 30,
              borderRadius: 8,
              background: input.trim() && !typing ? '#7c5af5' : 'rgba(124,90,245,0.2)',
              border: 'none',
              cursor: input.trim() && !typing ? 'pointer' : 'default',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            <Send size={13} color="white" />
          </button>
        </div>
        <p style={{ textAlign: 'center', fontSize: 10, color: '#44445a', marginTop: 7, fontFamily: "'JetBrains Mono',monospace" }}>↵ to send</p>
      </div>
    </motion.div>
  );
}
