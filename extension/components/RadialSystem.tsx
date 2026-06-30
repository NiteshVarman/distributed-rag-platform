import { motion, AnimatePresence } from 'motion/react';
import { Sparkles, X, Loader2 } from 'lucide-react';
import { ALL_ITEMS } from '@/lib/tools';
import type { ActionId, OrbStage } from '@/lib/types';

// Fan geometry: 7 items on a generous arc so they never overlap. The arc opens
// away from the selected text — downward by default, upward when flipped.
const RADIUS = 124;
const BASE_ANGLES = [20, 43, 67, 90, 113, 137, 160];

export function RadialSystem({
  stage,
  direction,
  hoveredId,
  setHoveredId,
  onOrbClick,
  onAction,
  onAsk,
}: {
  stage: OrbStage;
  direction: 'down' | 'up';
  hoveredId: string | null;
  setHoveredId: (id: string | null) => void;
  onOrbClick: () => void;
  onAction: (id: ActionId) => void;
  onAsk: () => void;
}) {
  const ITEM = 42;
  const ORB = 50;
  const hi = ITEM / 2;
  const oh = ORB / 2;

  const offsets = BASE_ANGLES.map((deg) => {
    const a = (deg * Math.PI) / 180;
    const y = Math.round(RADIUS * Math.sin(a));
    return { x: Math.round(RADIUS * Math.cos(a)), y: direction === 'up' ? -y : y };
  });

  return (
    <>
      {/* Fan items */}
      <AnimatePresence>
        {stage === 'open' &&
          ALL_ITEMS.map((item, i) => {
            const { Icon } = item;
            const off = offsets[i];
            const isH = hoveredId === item.id;
            return (
              <motion.div
                key={item.id}
                style={{ position: 'absolute', left: -hi, top: -hi, width: ITEM, height: ITEM, zIndex: 30 }}
                initial={{ x: 0, y: 0, opacity: 0, scale: 0 }}
                animate={{ x: off.x, y: off.y, opacity: 1, scale: 1 }}
                exit={{ x: 0, y: 0, opacity: 0, scale: 0, transition: { duration: 0.12 } }}
                transition={{ delay: i * 0.03, type: 'spring', stiffness: 480, damping: 28 }}
              >
                <motion.button
                  style={{
                    width: '100%',
                    height: '100%',
                    borderRadius: '50%',
                    background: 'rgba(8,8,20,0.94)',
                    border: `1.5px solid ${item.color}50`,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    cursor: 'pointer',
                    position: 'relative',
                    backdropFilter: 'blur(16px)',
                    boxShadow: isH
                      ? `0 0 0 2px ${item.color}70, 0 0 22px ${item.glow}, 0 0 44px ${item.glow}`
                      : `0 0 16px ${item.glow}`,
                    transition: 'box-shadow 0.15s ease',
                  }}
                  whileHover={{ scale: 1.2 }}
                  whileTap={{ scale: 0.9 }}
                  onHoverStart={() => setHoveredId(item.id)}
                  onHoverEnd={() => setHoveredId(null)}
                  onClick={() => (item.id === 'ask' ? onAsk() : onAction(item.id as ActionId))}
                >
                  <Icon size={17} color={item.color} />
                </motion.button>

                {/* Hover label pill */}
                <AnimatePresence>
                  {isH && (
                    <motion.span
                      initial={{ opacity: 0, y: 4, scale: 0.85 }}
                      animate={{ opacity: 1, y: 0, scale: 1 }}
                      exit={{ opacity: 0, y: 4, scale: 0.85 }}
                      transition={{ duration: 0.11 }}
                      style={{
                        position: 'absolute',
                        ...(direction === 'up' ? { top: -26 } : { bottom: -26 }),
                        left: '50%',
                        transform: 'translateX(-50%)',
                        whiteSpace: 'nowrap',
                        background: 'rgba(8,8,20,0.97)',
                        border: `1px solid ${item.color}45`,
                        borderRadius: 6,
                        padding: '2px 9px',
                        fontSize: 10,
                        fontFamily: "'JetBrains Mono', monospace",
                        color: item.color,
                        letterSpacing: '0.05em',
                        boxShadow: '0 2px 10px rgba(0,0,0,0.5)',
                        pointerEvents: 'none',
                      }}
                    >
                      {item.label}
                    </motion.span>
                  )}
                </AnimatePresence>
              </motion.div>
            );
          })}
      </AnimatePresence>

      {/* Pulse ring */}
      {(stage === 'idle' || stage === 'done') && (
        <div
          style={{
            position: 'absolute',
            left: -(oh + 9),
            top: -(oh + 9),
            width: ORB + 18,
            height: ORB + 18,
            borderRadius: '50%',
            border: '1.5px solid rgba(124,90,245,0.35)',
            animation: 'nx-orbPulse 2.2s ease-out infinite',
            pointerEvents: 'none',
          }}
        />
      )}

      {/* Core orb */}
      <motion.button
        style={{
          position: 'absolute',
          left: -oh,
          top: -oh,
          width: ORB,
          height: ORB,
          borderRadius: '50%',
          background:
            stage === 'open'
              ? 'rgba(8,8,20,0.97)'
              : 'linear-gradient(135deg, #7c5af5 0%, #3b82f6 55%, #00d4ff 100%)',
          border: stage === 'open' ? '1.5px solid rgba(255,255,255,0.1)' : '1.5px solid rgba(255,255,255,0.22)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: 'pointer',
          zIndex: 40,
          boxShadow:
            stage === 'open'
              ? '0 0 0 1px rgba(124,90,245,0.25), 0 4px 20px rgba(0,0,0,0.5)'
              : '0 0 0 8px rgba(124,90,245,0.1), 0 0 32px rgba(124,90,245,0.4), 0 0 64px rgba(0,212,255,0.15)',
        }}
        whileHover={{ scale: 1.08 }}
        whileTap={{ scale: 0.94 }}
        onClick={onOrbClick}
      >
        <AnimatePresence mode="wait">
          {stage === 'loading' ? (
            <motion.div key="spin" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
              <Loader2 size={20} color="white" style={{ animation: 'nx-spin 0.9s linear infinite' }} />
            </motion.div>
          ) : stage === 'open' ? (
            <motion.div key="x" initial={{ opacity: 0, rotate: 45 }} animate={{ opacity: 1, rotate: 0 }} exit={{ opacity: 0 }}>
              <X size={18} color="rgba(180,180,210,0.75)" />
            </motion.div>
          ) : (
            <motion.div key="spark" initial={{ opacity: 0, scale: 0.7 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0 }}>
              <Sparkles size={19} color="white" />
            </motion.div>
          )}
        </AnimatePresence>
      </motion.button>
    </>
  );
}
