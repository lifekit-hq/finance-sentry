/** SSE event names emitted by GET/POST /agent/chat (must match the backend controller). */
export const AGENT_SSE_EVENTS = {
  conversation: 'conversation',
  text: 'text',
  tool: 'tool',
  error: 'error',
  done: 'done',
} as const;

/** Default user-facing error message when no registry entry resolves. */
export const AGENT_DEFAULT_ERROR = 'Ledger is unavailable right now. Try again.';
