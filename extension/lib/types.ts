export type ActionId =
  | 'summarize'
  | 'explain'
  | 'translate'
  | 'rewrite'
  | 'expand'
  | 'grammar';

export type OrbStage = 'hidden' | 'idle' | 'open' | 'loading' | 'done';

/** Message sent from content script → background service worker. */
export interface TextActionMessage {
  type: 'TEXT_ACTION';
  action: ActionId | 'answer';
  text: string;
  targetLanguage?: string | null;
  pageUrl?: string | null;
}

/** Response from the background service worker. */
export interface TextActionResponse {
  ok: boolean;
  result?: string;
  mode?: 'rag' | 'general' | 'n-a';
  action?: string;
  sources?: { chunk: string; chunkType: string; score: number }[];
  error?: string;
}
