import type { ActionId, TextActionResponse } from './types';

export interface TextActionResult {
  result: string;
  mode: 'rag' | 'general' | 'n-a';
  sources: { chunk: string; chunkType: string; score: number }[];
}

/**
 * Runs an LLM text action by delegating to the background service worker.
 * The worker (chrome-extension:// origin) proxies the call to the API, which
 * sidesteps the mixed-content block an https page would otherwise hit.
 */
export async function runTextAction(
  action: ActionId | 'answer',
  text: string,
  opts?: { targetLanguage?: string; pageUrl?: string },
): Promise<TextActionResult> {
  const resp: TextActionResponse = await chrome.runtime.sendMessage({
    type: 'TEXT_ACTION',
    action,
    text,
    targetLanguage: opts?.targetLanguage ?? null,
    pageUrl: opts?.pageUrl ?? null,
  });

  if (!resp || !resp.ok) {
    throw new Error(resp?.error || 'Could not reach the AI service. Is the API running?');
  }
  return {
    result: resp.result ?? '',
    mode: resp.mode ?? 'n-a',
    sources: resp.sources ?? [],
  };
}

/** Generic worker call that throws on failure and returns the payload. */
async function send<T = any>(message: object): Promise<T> {
  const resp: any = await chrome.runtime.sendMessage(message);
  if (!resp || !resp.ok) throw new Error(resp?.error || 'Request failed');
  return resp as T;
}

export interface PageStatus {
  exists: boolean;
  processed: boolean;
  status?: string;
  progress: number;
  currentStep?: string;
  taskId?: string;
}

/** Whether a page is already ingested, plus the latest task's progress. */
export function getPageStatus(url: string) {
  return send<PageStatus>({ type: 'PAGE_STATUS', url });
}

/** Queue a page for ingestion; returns the new taskId. */
export function processPage(url: string) {
  return send<{ taskId: string; status: string }>({ type: 'PROCESS_URL', url });
}

/** Poll a processing task's status. */
export function getJobStatus(taskId: string) {
  return send<{ status: string; progress: number; currentStep: string; error?: string }>({
    type: 'JOB_STATUS',
    taskId,
  });
}
