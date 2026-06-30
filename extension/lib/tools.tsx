import {
  Sparkles,
  BookOpen,
  Languages,
  PenLine,
  Maximize2,
  CheckSquare,
  MessageSquare,
} from 'lucide-react';
import type { ActionId } from './types';

export interface ToolDef {
  id: ActionId | 'ask';
  label: string;
  Icon: typeof Sparkles;
  color: string;
  glow: string;
}

export const TOOLS: ToolDef[] = [
  { id: 'summarize', label: 'Summarize', Icon: Sparkles, color: '#a78bfa', glow: 'rgba(167,139,250,0.45)' },
  { id: 'explain', label: 'Explain', Icon: BookOpen, color: '#22d3ee', glow: 'rgba(34,211,238,0.45)' },
  { id: 'translate', label: 'Translate', Icon: Languages, color: '#f472b6', glow: 'rgba(244,114,182,0.45)' },
  { id: 'rewrite', label: 'Rewrite', Icon: PenLine, color: '#fbbf24', glow: 'rgba(251,191,36,0.45)' },
  { id: 'expand', label: 'Expand', Icon: Maximize2, color: '#34d399', glow: 'rgba(52,211,153,0.45)' },
  { id: 'grammar', label: 'Grammar', Icon: CheckSquare, color: '#fb923c', glow: 'rgba(251,146,60,0.45)' },
];

export const ASK_ITEM: ToolDef = {
  id: 'ask',
  label: 'Ask AI',
  Icon: MessageSquare,
  color: '#00d4ff',
  glow: 'rgba(0,212,255,0.45)',
};

export const ALL_ITEMS: ToolDef[] = [...TOOLS, ASK_ITEM];

export const LANGUAGES = [
  'Spanish',
  'French',
  'German',
  'Japanese',
  'Chinese',
  'Arabic',
  'Portuguese',
  'Hindi',
];
