import {DateUtils} from '../../../shared/utils/date.utils';
import {
  CARD_EXPIRED_NOTE,
  CARD_VALID_TO_PATTERN,
  CLOSED_REASON_ABSENT,
  CLOSED_REASON_BY_YOU,
  CLOSED_REASON_REISSUED,
  CLOSED_REASON_REISSUED_LABEL,
  MONTHS_PER_YEAR,
} from '../constants/account-lifecycle/account-lifecycle.constants';
import {
  type AccountNote,
  type ClosedAccountRow,
} from '../models/account-lifecycle/account-lifecycle.model';
import {type BankAccount} from '../models/bank-account/bank-account.model';

/** Wording for the card and account lifecycle: the closed section's rows and the quiet notes on live rows. */
export class AccountLifecycleUtils {
  /**
   * "Card expires 01/2027" for a TrueLayer card whose `valid_to` is still ahead, the expired wording once that
   * month is over; null for anything else (no expiry sent, or not a TrueLayer card).
   */
  public static expiryNote(
    account: Pick<BankAccount, 'provider' | 'cardValidTo'>,
    now: Date
  ): Nullable<string> {
    if (account.provider !== 'truelayer' || !account.cardValidTo) {
      return null;
    }
    const match = CARD_VALID_TO_PATTERN.exec(account.cardValidTo);
    if (!match) {
      return null;
    }
    const year = Number(match[1]);
    const month = Number(match[2]);
    if (month < 1 || month > MONTHS_PER_YEAR) {
      return null;
    }
    // A card is good through the last day of its valid_to month.
    const expired =
      now.getFullYear() * MONTHS_PER_YEAR + now.getMonth() > year * MONTHS_PER_YEAR + month - 1;
    return expired ? CARD_EXPIRED_NOTE : `Card expires ${match[2]}/${match[1]}`;
  }

  /** "New card •••• 1234 (replaced •••• 9312)" when this account took over a retired one; else null. */
  public static replacementNote(
    account: Pick<BankAccount, 'accountNumberLast4' | 'predecessorAccountId' | 'previousLast4'>,
    byId: ReadonlyMap<string, BankAccount>
  ): Nullable<string> {
    const predecessor = account.predecessorAccountId
      ? byId.get(account.predecessorAccountId)
      : undefined;
    // A new account linked by PredecessorAccountId wins; otherwise the same account's own previous last four.
    const replaced = predecessor?.accountNumberLast4 ?? account.previousLast4;
    return replaced
      ? `New card •••• ${account.accountNumberLast4} (replaced •••• ${replaced})`
      : null;
  }

  public static note(
    account: BankAccount,
    byId: ReadonlyMap<string, BankAccount>,
    now: Date
  ): AccountNote {
    return {
      expiry: AccountLifecycleUtils.expiryNote(account, now),
      replacement: AccountLifecycleUtils.replacementNote(account, byId),
    };
  }

  public static closedReasonLabel(reason: Nullable<string> | undefined, bankName: string): string {
    if (reason === CLOSED_REASON_ABSENT) {
      return `No longer listed by ${bankName}`;
    }
    if (reason === CLOSED_REASON_REISSUED) {
      return CLOSED_REASON_REISSUED_LABEL;
    }
    // Only a sync closes an account today; any other reason is the owner's own doing.
    return CLOSED_REASON_BY_YOU;
  }

  /** "Black · UAH": a Monobank card type, "Card" for a TrueLayer card, else the account type. */
  public static kindLabel(
    account: Pick<BankAccount, 'productType' | 'accountType' | 'currency'>
  ): string {
    const raw = account.productType || account.accountType;
    const kind = raw.charAt(0).toUpperCase() + raw.slice(1);
    return `${kind} · ${account.currency}`;
  }

  public static closedRow(account: BankAccount): ClosedAccountRow {
    const closed = account.closedAt ? new Date(account.closedAt) : null;
    const closedLabel =
      closed && !Number.isNaN(closed.getTime())
        ? `Closed ${DateUtils.dayLabel(DateUtils.isoDateLocal(closed), true)}`
        : 'Closed';
    return {
      accountId: account.accountId,
      bankName: account.bankName,
      kindLabel: AccountLifecycleUtils.kindLabel(account),
      last4: account.accountNumberLast4,
      closedLabel,
      reasonLabel: AccountLifecycleUtils.closedReasonLabel(account.closedReason, account.bankName),
      lastBalance: account.currentBalance,
      currency: account.currency,
      reopenable: account.closedReason !== CLOSED_REASON_REISSUED,
    };
  }

  /** Newest closure first. */
  public static closedRows(accounts: readonly BankAccount[]): ClosedAccountRow[] {
    return accounts
      .filter(account => !!account.closedAt)
      .sort((a, b) => Date.parse(b.closedAt ?? '') - Date.parse(a.closedAt ?? ''))
      .map(account => AccountLifecycleUtils.closedRow(account));
  }
}
