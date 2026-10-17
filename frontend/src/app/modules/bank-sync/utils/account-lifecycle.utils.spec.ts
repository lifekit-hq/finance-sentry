import {describe, expect, it} from 'vitest';

import {
  CARD_EXPIRED_NOTE,
  CLOSED_REASON_BY_YOU,
} from '../constants/account-lifecycle/account-lifecycle.constants';
import {type BankAccount} from '../models/bank-account/bank-account.model';
import {AccountLifecycleUtils} from './account-lifecycle.utils';

function account(overrides: Partial<BankAccount> = {}): BankAccount {
  return {
    accountId: 'a1',
    bankName: 'Monobank',
    accountType: 'checking',
    accountNumberLast4: '1234',
    currency: 'UAH',
    provider: 'monobank',
    ownerName: 'Test Owner',
    currentBalance: 250,
    availableBalance: 250,
    syncStatus: 'active',
    lastSyncTimestamp: null,
    lastSyncDurationMs: null,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const JUNE_2026 = new Date(2026, 5, 15);

describe('AccountLifecycleUtils.expiryNote', () => {
  const card = (cardValidTo?: Nullable<string>) =>
    account({provider: 'truelayer', cardValidTo, productType: 'card'});

  it('says when a card still ahead of its date expires', () => {
    expect(AccountLifecycleUtils.expiryNote(card('2027-01'), JUNE_2026)).toBe(
      'Card expires 01/2027'
    );
  });

  it('keeps the expiry line through the last month of validity', () => {
    expect(AccountLifecycleUtils.expiryNote(card('2026-06'), JUNE_2026)).toBe(
      'Card expires 06/2026'
    );
  });

  it('says the card expired once its month is over', () => {
    expect(AccountLifecycleUtils.expiryNote(card('2026-05'), JUNE_2026)).toBe(CARD_EXPIRED_NOTE);
  });

  it('reads a year-month-day valid_to by its month', () => {
    expect(AccountLifecycleUtils.expiryNote(card('2027-01-31'), JUNE_2026)).toBe(
      'Card expires 01/2027'
    );
  });

  it('shows nothing without an expiry, for a non-TrueLayer account or for a value it cannot read', () => {
    expect(AccountLifecycleUtils.expiryNote(card(null), JUNE_2026)).toBeNull();
    expect(AccountLifecycleUtils.expiryNote(card(undefined), JUNE_2026)).toBeNull();
    expect(
      AccountLifecycleUtils.expiryNote(
        account({provider: 'monobank', cardValidTo: '2027-01'}),
        JUNE_2026
      )
    ).toBeNull();
    expect(AccountLifecycleUtils.expiryNote(card('soon'), JUNE_2026)).toBeNull();
    expect(AccountLifecycleUtils.expiryNote(card('2027-13'), JUNE_2026)).toBeNull();
  });
});

describe('AccountLifecycleUtils.replacementNote', () => {
  it('names the new and the replaced card', () => {
    const old = account({accountId: 'old', accountNumberLast4: '9312'});
    const next = account({
      accountId: 'new',
      accountNumberLast4: '1234',
      predecessorAccountId: 'old',
    });

    expect(AccountLifecycleUtils.replacementNote(next, new Map([['old', old]]))).toBe(
      'New card •••• 1234 (replaced •••• 9312)'
    );
  });

  it('shows nothing without a predecessor, or when it is not in the list', () => {
    expect(AccountLifecycleUtils.replacementNote(account(), new Map())).toBeNull();
    expect(
      AccountLifecycleUtils.replacementNote(account({predecessorAccountId: 'gone'}), new Map())
    ).toBeNull();
  });
});

describe('AccountLifecycleUtils.closedReasonLabel', () => {
  it('words each reason the sync records', () => {
    expect(AccountLifecycleUtils.closedReasonLabel('absent_from_list', 'Monobank')).toBe(
      'No longer listed by Monobank'
    );
    expect(AccountLifecycleUtils.closedReasonLabel('reissued', 'Monobank')).toBe(
      'Replaced by a new card'
    );
  });

  it('reads any other reason as the owner closing it', () => {
    expect(AccountLifecycleUtils.closedReasonLabel('user', 'Monobank')).toBe(CLOSED_REASON_BY_YOU);
    expect(AccountLifecycleUtils.closedReasonLabel(null, 'Monobank')).toBe(CLOSED_REASON_BY_YOU);
  });
});

describe('AccountLifecycleUtils.kindLabel', () => {
  it('leads with the Monobank card type, then the currency', () => {
    expect(AccountLifecycleUtils.kindLabel(account({productType: 'black'}))).toBe('Black · UAH');
  });

  it('falls back to the account type when no card type is known', () => {
    expect(AccountLifecycleUtils.kindLabel(account({productType: null}))).toBe('Checking · UAH');
  });
});

describe('AccountLifecycleUtils.closedRows', () => {
  it('lists only closed accounts, newest closure first, with the last balance', () => {
    const rows = AccountLifecycleUtils.closedRows([
      account({accountId: 'live'}),
      account({accountId: 'older', closedAt: '2026-08-01T10:00:00Z', closedReason: 'reissued'}),
      account({
        accountId: 'newer',
        closedAt: '2026-10-12T10:00:00Z',
        closedReason: 'absent_from_list',
        currentBalance: 42,
      }),
    ]);

    expect(rows.map(r => r.accountId)).toEqual(['newer', 'older']);
    expect(rows[0]).toMatchObject({
      reasonLabel: 'No longer listed by Monobank',
      lastBalance: 42,
      currency: 'UAH',
      last4: '1234',
    });
    expect(rows[0].closedLabel).toMatch(/^Closed .*2026$/);
  });

  it('lets an absent card reopen but not one replaced by a live successor', () => {
    const [replaced, absent] = AccountLifecycleUtils.closedRows([
      account({
        accountId: 'absent',
        closedAt: '2026-08-01T10:00:00Z',
        closedReason: 'absent_from_list',
      }),
      account({accountId: 'replaced', closedAt: '2026-10-01T10:00:00Z', closedReason: 'reissued'}),
    ]);

    expect(replaced.reopenable).toBe(false);
    expect(absent.reopenable).toBe(true);
  });

  it('words a closure date it cannot read as just Closed', () => {
    expect(AccountLifecycleUtils.closedRow(account({closedAt: 'nonsense'})).closedLabel).toBe(
      'Closed'
    );
  });
});

describe('AccountLifecycleUtils.note', () => {
  it('carries both lines for one account', () => {
    const old = account({accountId: 'old', accountNumberLast4: '9312'});
    const next = account({
      accountId: 'new',
      provider: 'truelayer',
      cardValidTo: '2027-01',
      predecessorAccountId: 'old',
    });

    expect(AccountLifecycleUtils.note(next, new Map([['old', old]]), JUNE_2026)).toEqual({
      expiry: 'Card expires 01/2027',
      replacement: 'New card •••• 1234 (replaced •••• 9312)',
    });
  });
});
