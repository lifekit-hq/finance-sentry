import {type AccountCategory} from '../../../shared/models/wealth/wealth.model';
import {
  ACCOUNTS_COLLAPSED_STORAGE_KEY,
  ACCOUNTS_DEFAULT_OPEN,
  ACCOUNTS_OPEN_STORAGE_KEY,
} from '../constants/accounts-disclosure/accounts-disclosure.constants';

/** The per-device open/closed choices of Accounts groups; storage can be blocked, so every access is guarded. */
export class AccountsDisclosureUtils {
  /** Stable key for an institution group; an institution id alone can repeat across providers. */
  public static key(institution: {provider: string; institutionId: string}): string {
    return `${institution.provider}:${institution.institutionId}`;
  }

  /** A group no one has toggled on this device: open for banking, closed for a broker or exchange. */
  public static defaultOpen(category: AccountCategory): boolean {
    return ACCOUNTS_DEFAULT_OPEN[category];
  }

  /**
   * The groups this device opened or closed by hand, by key; empty when none, unreadable or storage is
   * unavailable. The earlier list of collapsed keys is folded in as closed, so a choice made before the
   * record existed still holds.
   */
  public static read(): Record<string, boolean> {
    return {
      ...Object.fromEntries(
        AccountsDisclosureUtils.readCollapsed().map(key => [key, false] as const)
      ),
      ...AccountsDisclosureUtils.readOpen(),
    };
  }

  public static write(open: Record<string, boolean>): void {
    try {
      localStorage.setItem(ACCOUNTS_OPEN_STORAGE_KEY, JSON.stringify(open));
    } catch {
      // Storage blocked: the group returns to its default on the next visit, nothing else depends on it.
    }
  }

  private static readOpen(): Record<string, boolean> {
    try {
      const parsed: unknown = JSON.parse(localStorage.getItem(ACCOUNTS_OPEN_STORAGE_KEY) ?? '{}');
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        return {};
      }
      return Object.fromEntries(
        Object.entries(parsed).filter(
          (entry): entry is [string, boolean] => typeof entry[1] === 'boolean'
        )
      );
    } catch {
      return {};
    }
  }

  private static readCollapsed(): string[] {
    try {
      const parsed: unknown = JSON.parse(
        localStorage.getItem(ACCOUNTS_COLLAPSED_STORAGE_KEY) ?? '[]'
      );
      return Array.isArray(parsed) ? parsed.filter(item => typeof item === 'string') : [];
    } catch {
      return [];
    }
  }
}
