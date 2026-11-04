import {formatRelativeTime} from '@lifekit-hq/core/format';
import {type Nullable} from '@lifekit-hq/ui';

import {type Institution} from '../../../shared/models/wealth/wealth.model';
import {SyncStatusLabelPipe} from '../../../shared/pipes/sync-status-label.pipe';
import {DateUtils} from '../../../shared/utils/date.utils';
import {ProviderUtils} from '../../../shared/utils/provider.utils';
import {
  CARD_VALID_TO_PATTERN,
  CLOSED_REASON_REISSUED,
} from '../constants/account-lifecycle/account-lifecycle.constants';
import {type BankAccount} from '../models/bank-account/bank-account.model';
import {type ConnectionDetailRow} from '../models/connection-details/connection-details.model';
import {AccountLifecycleUtils} from './account-lifecycle.utils';

const MS_PER_SECOND = 1000;
const SECONDS_FRACTION_DIGITS = 1;
const NEVER = 'Never';

/** The lines of the read-only "Connection details" sheet, worded from what the accounts load already carries. */
export class ConnectionDetailsUtils {
  /** "420 ms" under a second, "2.4 s" above it. */
  public static duration(ms: number): string {
    return ms < MS_PER_SECOND
      ? `${ms} ms`
      : `${(ms / MS_PER_SECOND).toFixed(SECONDS_FRACTION_DIGITS)} s`;
  }

  /** "01/2027" for a year-month `valid_to` (a trailing day is ignored); null when it is not one. */
  public static validTo(cardValidTo: Nullable<string> | undefined): Nullable<string> {
    const match = cardValidTo ? CARD_VALID_TO_PATTERN.exec(cardValidTo) : null;
    return match ? `${match[2]}/${match[1]}` : null;
  }

  /** "Mon 12 Oct 2026" for an instant, in the viewer's zone; the raw text when it is not a date. */
  public static day(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
      ? iso
      : DateUtils.dayLabel(DateUtils.isoDateLocal(date), true);
  }

  /**
   * An institution's sheet: its provider, health and last sync, then what the bank-account list adds for the
   * accounts under it (sync duration, card products, card expiry). Brokers and exchanges have no such accounts,
   * so their sheet stops at the sync lines.
   */
  public static institutionRows(
    institution: Institution,
    accounts: readonly BankAccount[]
  ): ConnectionDetailRow[] {
    const ids = new Set(
      [...institution.accounts, ...(institution.cards ?? []).flatMap(card => card.accounts)].map(
        account => account.accountId
      )
    );
    const linked = accounts.filter(account => ids.has(account.accountId) && !account.closedAt);
    const rows: ConnectionDetailRow[] = [
      {label: 'Provider', value: ProviderUtils.label(institution.provider)},
      {label: 'Status', value: new SyncStatusLabelPipe().transform(institution.syncStatus)},
      {label: 'Last synced', value: ConnectionDetailsUtils.since(institution.lastSyncTimestamp)},
    ];
    if (
      institution.lastSuccessfulSyncTimestamp &&
      institution.lastSuccessfulSyncTimestamp !== institution.lastSyncTimestamp
    ) {
      rows.push({
        label: 'Last successful sync',
        value: ConnectionDetailsUtils.since(institution.lastSuccessfulSyncTimestamp),
      });
    }
    const duration = ConnectionDetailsUtils.latestDuration(linked);
    if (duration !== null) {
      rows.push({label: 'Sync took', value: ConnectionDetailsUtils.duration(duration)});
    }
    const products = [
      ...new Set(linked.flatMap(account => (account.productType ? [account.productType] : []))),
    ].map(product => product.charAt(0).toUpperCase() + product.slice(1));
    if (products.length > 0) {
      rows.push({label: 'Products', value: products.join(', ')});
    }
    for (const account of linked) {
      const validTo = ConnectionDetailsUtils.validTo(account.cardValidTo);
      if (validTo) {
        rows.push({label: `Card •••• ${account.accountNumberLast4}`, value: `Valid to ${validTo}`});
      }
    }
    return rows;
  }

  /** A closed account's sheet: what it was, how it ended, when it last synced and which card replaced it. */
  public static closedAccountRows(
    account: BankAccount,
    byId: ReadonlyMap<string, BankAccount>
  ): ConnectionDetailRow[] {
    const closed = AccountLifecycleUtils.closedRow(account);
    const rows: ConnectionDetailRow[] = [
      {label: 'Provider', value: ProviderUtils.label(account.provider)},
      {label: 'Account', value: closed.kindLabel},
      {label: 'Card number', value: `•••• ${account.accountNumberLast4}`},
      {label: 'Status', value: closed.closedLabel},
      {label: 'Reason', value: closed.reasonLabel},
      {label: 'Last synced', value: ConnectionDetailsUtils.since(account.lastSyncTimestamp)},
    ];
    if (account.lastSyncDurationMs !== null && account.lastSyncDurationMs !== undefined) {
      rows.push({
        label: 'Sync took',
        value: ConnectionDetailsUtils.duration(account.lastSyncDurationMs),
      });
    }
    const validTo = ConnectionDetailsUtils.validTo(account.cardValidTo);
    if (validTo) {
      rows.push({label: 'Card valid to', value: validTo});
    }
    if (account.absentSince) {
      rows.push({
        label: 'Missing from provider since',
        value: ConnectionDetailsUtils.day(account.absentSince),
      });
    }
    if (account.panChangedAt) {
      rows.push({
        label: 'Card number changed',
        value: ConnectionDetailsUtils.day(account.panChangedAt),
      });
    }
    const replacement = ConnectionDetailsUtils.replacedBy(account, byId);
    if (replacement) {
      rows.push({label: 'Replaced by', value: replacement});
    }
    const predecessor = account.predecessorAccountId
      ? byId.get(account.predecessorAccountId)?.accountNumberLast4
      : (account.previousLast4 ?? null);
    if (predecessor) {
      rows.push({label: 'Replaced card', value: `•••• ${predecessor}`});
    }
    return rows;
  }

  private static since(iso: Nullable<string>): string {
    return iso ? formatRelativeTime(iso) : NEVER;
  }

  /** The newest sync's duration among `accounts`; null when none of them recorded one. */
  private static latestDuration(accounts: readonly BankAccount[]): Nullable<number> {
    const timed = accounts
      .filter(a => a.lastSyncDurationMs !== null && a.lastSyncDurationMs !== undefined)
      .sort(
        (a, b) => Date.parse(b.lastSyncTimestamp ?? '') - Date.parse(a.lastSyncTimestamp ?? '')
      );
    return timed[0]?.lastSyncDurationMs ?? null;
  }

  /** The live card that took over a card closed because it was reissued; null for any other closure. */
  private static replacedBy(
    account: BankAccount,
    byId: ReadonlyMap<string, BankAccount>
  ): Nullable<string> {
    if (account.closedReason !== CLOSED_REASON_REISSUED) {
      return null;
    }
    const successor = [...byId.values()].find(
      candidate => candidate.predecessorAccountId === account.accountId && !candidate.closedAt
    );
    return successor ? `•••• ${successor.accountNumberLast4}` : null;
  }
}
