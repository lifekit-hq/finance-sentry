import {ACCOUNTS_COLLAPSED_STORAGE_KEY} from '../constants/accounts-disclosure/accounts-disclosure.constants';

/** The per-device set of collapsed Accounts groups; storage can be blocked, so every access is guarded. */
export class AccountsDisclosureUtils {
  /** Stable key for an institution group; an institution id alone can repeat across providers. */
  public static key(institution: {provider: string; institutionId: string}): string {
    return `${institution.provider}:${institution.institutionId}`;
  }

  /** The keys this device collapsed; empty when none, unreadable or storage is unavailable. */
  public static read(): string[] {
    try {
      const parsed: unknown = JSON.parse(
        localStorage.getItem(ACCOUNTS_COLLAPSED_STORAGE_KEY) ?? '[]'
      );
      return Array.isArray(parsed) ? parsed.filter(item => typeof item === 'string') : [];
    } catch {
      return [];
    }
  }

  public static write(collapsed: string[]): void {
    try {
      localStorage.setItem(ACCOUNTS_COLLAPSED_STORAGE_KEY, JSON.stringify(collapsed));
    } catch {
      // Storage blocked: the group reopens on the next visit, nothing else depends on it.
    }
  }
}
