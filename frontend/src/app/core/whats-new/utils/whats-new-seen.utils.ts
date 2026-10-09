import {WHATS_NEW_SEEN_STORAGE_KEY} from '../constants/whats-new.constants';

/** The per-device "last seen" version; storage can be blocked, so every access is guarded. */
export class WhatsNewSeenUtils {
  /** The version recorded on this device; null when none or storage is unavailable. */
  public static read(): Nullable<string> {
    try {
      return localStorage.getItem(WHATS_NEW_SEEN_STORAGE_KEY);
    } catch {
      return null;
    }
  }

  public static write(version: string): void {
    try {
      localStorage.setItem(WHATS_NEW_SEEN_STORAGE_KEY, version);
    } catch {
      // Storage blocked: the dot can reappear on the next visit, nothing else depends on it.
    }
  }
}
