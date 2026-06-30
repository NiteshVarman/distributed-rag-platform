import { useState } from 'react';
import { motion } from 'motion/react';
import { X, Copy, ThumbsUp, ThumbsDown, RotateCcw } from 'lucide-react';
import { TOOLS } from '@/lib/tools';
import type { ActionId } from '@/lib/types';

export function ResultCard({
  action,
  text,
  isError,
  badge,
  onClose,
  onRetry,
  pos,
}: {
  action: ActionId;
  text: string;
  isError?: boolean;
  badge?: 'rag' | 'general' | null;
  onClose: () => void;
  onRetry: () => void;
  pos: { x: number; y: number };
}) {
  const tool = TOOLS.find((t) => t.id === action)!;
  const [copied, setCopied] = useState(false);

  const copy = () => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const left = Math.max(8, Math.min(pos.x - 160, window.innerWidth - 328));
  const top = Math.min(pos.y + 42, window.innerHeight - 260);

  return (
    <motion.div
      initial={{ opacity: 0, y: -8, scale: 0.96 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: -8, scale: 0.96 }}
      transition={{ type: 'spring', stiffness: 420, damping: 32 }}
      onMouseDown={(e) => e.stopPropagation()}
      style={{
        position: 'fixed',
        left,
        top,
        width: 320,
        zIndex: 2147483646,
        pointerEvents: 'auto',
        background: 'rgba(8,8,20,0.98)',
        border: `1px solid ${tool.color}28`,
        borderRadius: 16,
        overflow: 'hidden',
        boxShadow: `0 12px 48px rgba(0,0,0,0.55), 0 0 0 1px ${tool.color}14, 0 0 32px ${tool.glow}`,
        backdropFilter: 'blur(24px)',
      }}
    >
      <div style={{ height: 2, background: `linear-gradient(90deg, transparent, ${tool.color}, transparent)` }} />

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 14px',
          borderBottom: '1px solid rgba(255,255,255,0.055)',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
          <tool.Icon size={13} color={tool.color} />
          <span
            style={{
              fontSize: 10,
              fontFamily: "'JetBrains Mono',monospace",
              color: tool.color,
              letterSpacing: '0.09em',
              textTransform: 'uppercase',
            }}
          >
            {tool.label}
          </span>
          {badge && (
            <span
              style={{
                fontSize: 9,
                fontFamily: "'JetBrains Mono',monospace",
                padding: '2px 7px',
                borderRadius: 999,
                letterSpacing: '0.04em',
                color: badge === 'rag' ? '#34d399' : '#9aa0b4',
                background: badge === 'rag' ? 'rgba(52,211,153,0.16)' : 'rgba(148,163,184,0.16)',
              }}
            >
              {badge === 'rag' ? 'FROM PAGE' : 'GENERAL'}
            </span>
          )}
        </div>
        <div style={{ display: 'flex', gap: 3 }}>
          <button
            onClick={copy}
            style={{
              padding: '4px 7px',
              borderRadius: 7,
              background: copied ? 'rgba(52,211,153,0.15)' : 'rgba(255,255,255,0.06)',
              border: 'none',
              cursor: 'pointer',
              color: copied ? '#34d399' : '#777799',
              display: 'flex',
              alignItems: 'center',
            }}
          >
            <Copy size={12} />
          </button>
          <button
            onClick={onClose}
            style={{
              padding: '4px 7px',
              borderRadius: 7,
              background: 'rgba(255,255,255,0.06)',
              border: 'none',
              cursor: 'pointer',
              color: '#777799',
              display: 'flex',
              alignItems: 'center',
            }}
          >
            <X size={12} />
          </button>
        </div>
      </div>

      <div
        style={{
          padding: '13px 15px',
          fontSize: 13,
          lineHeight: 1.78,
          color: isError ? '#fca5a5' : 'rgba(238,238,255,0.87)',
          fontFamily: "'Inter',sans-serif",
          maxHeight: 280,
          overflowY: 'auto',
        }}
      >
        {text.split('\n').map((line, i) => (
          <p key={i} style={{ marginBottom: line.trim() ? 3 : 0 }}>
            {line}
          </p>
        ))}
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          padding: '8px 14px',
          borderTop: '1px solid rgba(255,255,255,0.055)',
          gap: 6,
        }}
      >
        <span style={{ fontSize: 11, color: '#55557a', fontFamily: "'JetBrains Mono',monospace" }}>Helpful?</span>
        <button style={{ padding: '3px 6px', borderRadius: 5, background: 'none', border: 'none', cursor: 'pointer', color: '#55557a' }}>
          <ThumbsUp size={12} />
        </button>
        <button style={{ padding: '3px 6px', borderRadius: 5, background: 'none', border: 'none', cursor: 'pointer', color: '#55557a' }}>
          <ThumbsDown size={12} />
        </button>
        <button
          onClick={onRetry}
          style={{
            marginLeft: 'auto',
            display: 'flex',
            alignItems: 'center',
            gap: 4,
            fontSize: 11,
            color: '#55557a',
            background: 'none',
            border: 'none',
            cursor: 'pointer',
            fontFamily: "'JetBrains Mono',monospace",
          }}
        >
          <RotateCcw size={11} /> Retry
        </button>
      </div>
    </motion.div>
  );
}
