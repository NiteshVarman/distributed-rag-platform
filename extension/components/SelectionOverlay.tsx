import { useState, useCallback, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { RadialSystem } from './RadialSystem';
import { ResultCard } from './ResultCard';
import { TranslatePicker } from './TranslatePicker';
import { ChatPanel } from './ChatPanel';
import { runTextAction } from '@/lib/api';
import type { ActionId, OrbStage } from '@/lib/types';

const MIN_SELECTION = 4;

export function SelectionOverlay() {
  const [orbStage, setOrbStage] = useState<OrbStage>('hidden');
  const [orbPos, setOrbPos] = useState({ x: 0, y: 0 });
  const [orbDir, setOrbDir] = useState<'down' | 'up'>('down');
  const [selectedText, setSelectedText] = useState('');
  const [activeAction, setActiveAction] = useState<ActionId | null>(null);
  const [resultText, setResultText] = useState<string | null>(null);
  const [resultError, setResultError] = useState(false);
  const [showTranslate, setShowTranslate] = useState(false);
  const [chatOpen, setChatOpen] = useState(false);
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  const lastLangRef = useRef<string | null>(null);

  const dismiss = useCallback(() => {
    setOrbStage('hidden');
    setResultText(null);
    setResultError(false);
    setActiveAction(null);
    setShowTranslate(false);
  }, []);

  const handleMouseUp = useCallback(() => {
    const sel = window.getSelection();
    const text = sel ? sel.toString().trim() : '';
    if (!sel || sel.isCollapsed || text.length < MIN_SELECTION) return;
    const rect = sel.getRangeAt(0).getBoundingClientRect();
    if (rect.width === 0 && rect.height === 0) return;

    const ORB = 50;
    const GAP = 10;
    const FAN_REACH = 210; // vertical space the open fan + labels need
    // Keep the fan on-screen horizontally.
    const x = Math.max(150, Math.min(rect.left + rect.width / 2, window.innerWidth - 150));
    // Place the orb fully BELOW the selection (so it never covers the word);
    // flip above only when there isn't room below.
    const roomBelow = window.innerHeight - rect.bottom;
    const placeBelow = roomBelow >= FAN_REACH || roomBelow >= rect.top;
    const y = placeBelow ? rect.bottom + GAP + ORB / 2 : rect.top - GAP - ORB / 2;

    setSelectedText(text.slice(0, 10000));
    setOrbPos({ x, y });
    setOrbDir(placeBelow ? 'down' : 'up');
    setOrbStage('idle');
    setResultText(null);
    setResultError(false);
    setActiveAction(null);
    setShowTranslate(false);
  }, []);

  // Document-level listeners (overlay lives in a shadow root; interactive
  // children stopPropagation on mousedown so clicks inside our UI don't dismiss).
  useEffect(() => {
    const onDown = () => dismiss();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        dismiss();
        setChatOpen(false);
      }
    };
    const onScroll = () => {
      setOrbStage((s) => (s === 'hidden' ? s : 'hidden'));
      setShowTranslate(false);
      setResultText(null);
    };
    document.addEventListener('mouseup', handleMouseUp);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    window.addEventListener('scroll', onScroll, true);
    return () => {
      document.removeEventListener('mouseup', handleMouseUp);
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('scroll', onScroll, true);
    };
  }, [handleMouseUp, dismiss]);

  const runAndShow = useCallback(
    async (id: ActionId, opts?: { targetLanguage?: string }) => {
      setOrbStage('loading');
      setShowTranslate(false);
      setActiveAction(id);
      try {
        const { result } = await runTextAction(id, selectedText, {
          targetLanguage: opts?.targetLanguage,
          pageUrl: location.href,
        });
        setResultText(result);
        setResultError(false);
      } catch (err: any) {
        setResultText(err?.message || 'Something went wrong.');
        setResultError(true);
      } finally {
        setOrbStage('done');
      }
    },
    [selectedText],
  );

  const handleOrbClick = useCallback(() => {
    if (orbStage === 'idle' || orbStage === 'done') setOrbStage('open');
    else if (orbStage === 'open') setOrbStage(resultText ? 'done' : 'idle');
  }, [orbStage, resultText]);

  const handleAction = useCallback(
    (id: ActionId) => {
      if (id === 'translate') {
        setShowTranslate(true);
        setOrbStage('idle');
        return;
      }
      void runAndShow(id);
    },
    [runAndShow],
  );

  const handleTranslate = useCallback(
    (lang: string) => {
      lastLangRef.current = lang;
      void runAndShow('translate', { targetLanguage: lang });
    },
    [runAndShow],
  );

  const handleRetry = useCallback(() => {
    if (!activeAction) return;
    if (activeAction === 'translate' && lastLangRef.current) {
      void runAndShow('translate', { targetLanguage: lastLangRef.current });
    } else {
      void runAndShow(activeAction);
    }
  }, [activeAction, runAndShow]);

  return (
    <>
      <style>{KEYFRAMES}</style>

      <AnimatePresence>
        {orbStage !== 'hidden' && (
          <motion.div
            key="orb"
            initial={{ opacity: 0, scale: 0.5 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.5 }}
            transition={{ type: 'spring', stiffness: 500, damping: 30 }}
            onMouseDown={(e) => e.stopPropagation()}
            style={{ position: 'fixed', left: orbPos.x, top: orbPos.y, width: 0, height: 0, zIndex: 2147483645, pointerEvents: 'auto' }}
          >
            <RadialSystem
              stage={orbStage}
              direction={orbDir}
              hoveredId={hoveredId}
              setHoveredId={setHoveredId}
              onOrbClick={handleOrbClick}
              onAction={handleAction}
              onAsk={() => {
                setChatOpen(true);
                dismiss();
              }}
            />
          </motion.div>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {orbStage === 'done' && resultText && activeAction && (
          <ResultCard
            key="result"
            action={activeAction}
            text={resultText}
            isError={resultError}
            badge={null}
            onClose={dismiss}
            onRetry={handleRetry}
            pos={orbPos}
          />
        )}
      </AnimatePresence>

      <AnimatePresence>
        {showTranslate && (
          <TranslatePicker key="translate" pos={orbPos} onSelect={handleTranslate} onClose={() => setShowTranslate(false)} />
        )}
      </AnimatePresence>

      <AnimatePresence>{chatOpen && <ChatPanel key="chat" onClose={() => setChatOpen(false)} />}</AnimatePresence>
    </>
  );
}

const KEYFRAMES = `
  @keyframes nx-orbPulse {
    0%   { transform: scale(1);    opacity: 0.7; }
    70%  { transform: scale(1.55); opacity: 0; }
    100% { transform: scale(1.55); opacity: 0; }
  }
  @keyframes nx-spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  @keyframes nx-bounce { 0%, 80%, 100% { transform: translateY(0); } 40% { transform: translateY(-5px); } }
`;
