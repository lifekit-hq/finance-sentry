export interface SessionCacheState {
  /** The last data each keyed store held, in memory for the session; never written to the device. */
  entries: Record<string, unknown>;
}

export const initialSessionCacheState: SessionCacheState = {
  entries: {},
};
