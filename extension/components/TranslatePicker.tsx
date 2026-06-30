import { motion } from 'motion/react';
import { X } from 'lucide-react';
import { LANGUAGES } from '@/lib/tools';

export function TranslatePicker({
  pos,
  onSelect,
  onClose,
}: {
  pos: { x: number; y: number };
  onSelect: (l: string) => void;
  onClose: () => void;
}) {
  const left = Math.max(8, Math.min(pos.x - 110, window.innerWidth - 228));
  const top = Math.min(pos.y + 32, window.innerHeight - 220);

  return (
    <motion.div
      initial={{ opacity: 0, y: -8, scale: 0.95 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: -8, scale: 0.95 }}
      transition={{ type: 'spring', stiffness: 420, damping: 30 }}
      onMouseDown={(e) => e.stopPropagation()}
      style={{
        position: 'fixed',
        left,
        top,
        zIndex: 2147483646,
        width: 220,
        pointerEvents: 'auto',
        background: 'rgba(8,8,20,0.98)',
        border: '1px solid rgba(244,114,182,0.25)',
        borderRadius: 14,
        overflow: 'hidden',
        boxShadow: '0 10px 40px rgba(0,0,0,0.55), 0 0 24px rgba(244,114,182,0.12)',
        backdropFilter: 'blur(20px)',
      }}
    >
      <div style={{ height: 2, background: 'linear-gradient(90deg, transparent, #f472b6, transparent)' }} />
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '9px 12px',
          borderBottom: '1px solid rgba(255,255,255,0.055)',
        }}
      >
        <span
          style={{
            fontSize: 10,
            fontFamily: "'JetBrains Mono',monospace",
            color: '#f472b6',
            letterSpacing: '0.09em',
            textTransform: 'uppercase',
          }}
        >
          Translate to
        </span>
        <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#666680', display: 'flex' }}>
          <X size={13} />
        </button>
      </div>
      <div style={{ padding: '6px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 4 }}>
        {LANGUAGES.map((lang) => (
          <button
            key={lang}
            onClick={() => onSelect(lang)}
            style={{
              textAlign: 'left',
              padding: '8px 10px',
              borderRadius: 8,
              background: 'rgba(244,114,182,0.06)',
              border: '1px solid transparent',
              cursor: 'pointer',
              fontSize: 12,
              color: 'rgba(238,238,255,0.8)',
              fontFamily: "'Inter',sans-serif",
              fontWeight: 500,
              transition: 'all 0.12s ease',
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.background = 'rgba(244,114,182,0.14)';
              e.currentTarget.style.borderColor = 'rgba(244,114,182,0.3)';
              e.currentTarget.style.color = '#f472b6';
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.background = 'rgba(244,114,182,0.06)';
              e.currentTarget.style.borderColor = 'transparent';
              e.currentTarget.style.color = 'rgba(238,238,255,0.8)';
            }}
          >
            {lang}
          </button>
        ))}
      </div>
    </motion.div>
  );
}
