import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type Institution} from '../../../shared/models/wealth/wealth.model';
import {type BankAccount} from '../models/bank-account/bank-account.model';
import {ConnectionDetailsUtils} from './connection-details.utils';

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

function institution(overrides: Partial<Institution> = {}): Institution {
  return {
    institutionId: 'inst-1',
    provider: 'monobank',
    name: 'Monobank',
    category: 'banking',
    totalInBaseCurrency: 100,
    syncStatus: 'synced',
    lastSyncTimestamp: null,
    lastSuccessfulSyncTimestamp: null,
    accounts: [],
    ...overrides,
  };
}

const value = (rows: {label: string; value: string}[], label: string) =>
  rows.find(row => row.label === label)?.value;

describe('ConnectionDetailsUtils', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-20T12:00:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('duration', () => {
    it('words a sub-second sync in milliseconds', () => {
      expect(ConnectionDetailsUtils.duration(420)).toBe('420 ms');
    });

    it('words a longer sync in seconds', () => {
      expect(ConnectionDetailsUtils.duration(2450)).toBe('2.5 s');
    });
  });

  describe('validTo', () => {
    it('words a year-month as month/year, ignoring a trailing day', () => {
      expect(ConnectionDetailsUtils.validTo('2027-01')).toBe('01/2027');
      expect(ConnectionDetailsUtils.validTo('2027-01-31')).toBe('01/2027');
    });

    it.each([null, undefined, '', 'soon'])('is null for %s', input => {
      expect(ConnectionDetailsUtils.validTo(input)).toBeNull();
    });
  });

  describe('day', () => {
    it('words an instant as a calendar day', () => {
      expect(ConnectionDetailsUtils.day('2026-10-12T12:00:00Z')).toBe('Mon 12 Oct 2026');
    });

    it('keeps text that is not a date', () => {
      expect(ConnectionDetailsUtils.day('unknown')).toBe('unknown');
    });
  });

  describe('institutionRows', () => {
    it('names the provider, the status and when it last synced', () => {
      const rows = ConnectionDetailsUtils.institutionRows(
        institution({lastSyncTimestamp: '2026-10-20T11:00:00Z'}),
        []
      );

      expect(value(rows, 'Provider')).toBe('Monobank');
      expect(value(rows, 'Status')).toBe('Synced');
      expect(value(rows, 'Last synced')).toBe('1h ago');
    });

    it('says Never when it has not synced', () => {
      expect(value(ConnectionDetailsUtils.institutionRows(institution(), []), 'Last synced')).toBe(
        'Never'
      );
    });

    it('adds the last successful sync only when it differs from the last attempt', () => {
      const same = ConnectionDetailsUtils.institutionRows(
        institution({
          lastSyncTimestamp: '2026-10-20T11:00:00Z',
          lastSuccessfulSyncTimestamp: '2026-10-20T11:00:00Z',
        }),
        []
      );
      const differs = ConnectionDetailsUtils.institutionRows(
        institution({
          syncStatus: 'failed',
          lastSyncTimestamp: '2026-10-20T11:00:00Z',
          lastSuccessfulSyncTimestamp: '2026-10-19T12:00:00Z',
        }),
        []
      );

      expect(value(same, 'Last successful sync')).toBeUndefined();
      expect(value(differs, 'Last successful sync')).toBe('1d ago');
      expect(value(differs, 'Status')).toBe('Failed');
    });

    it("takes the sync duration from the newest sync among the institution's accounts", () => {
      const accounts = [
        account({
          accountId: 'old',
          lastSyncTimestamp: '2026-10-20T09:00:00Z',
          lastSyncDurationMs: 9000,
        }),
        account({
          accountId: 'new',
          lastSyncTimestamp: '2026-10-20T11:00:00Z',
          lastSyncDurationMs: 1200,
        }),
        account({
          accountId: 'other',
          lastSyncTimestamp: '2026-10-20T11:59:00Z',
          lastSyncDurationMs: 50,
        }),
      ];
      const rows = ConnectionDetailsUtils.institutionRows(
        institution({
          accounts: [{accountId: 'old'}, {accountId: 'new'}] as Institution['accounts'],
        }),
        accounts
      );

      expect(value(rows, 'Sync took')).toBe('1.2 s');
    });

    it('lists the card products and expiries of the accounts under it, skipping closed ones', () => {
      const accounts = [
        account({accountId: 'a', productType: 'black', accountNumberLast4: '1111'}),
        account({
          accountId: 'b',
          productType: 'card',
          accountNumberLast4: '2222',
          cardValidTo: '2027-01',
        }),
        account({accountId: 'c', productType: 'white', closedAt: '2026-09-01T00:00:00Z'}),
      ];
      const rows = ConnectionDetailsUtils.institutionRows(
        institution({
          cards: [
            {
              cardType: 'x',
              displayName: 'x',
              totalInBaseCurrency: 0,
              syncStatus: 'synced',
              accounts: [{accountId: 'a'}, {accountId: 'b'}, {accountId: 'c'}] as never,
            },
          ],
        }),
        accounts
      );

      expect(value(rows, 'Products')).toBe('Black, Card');
      expect(value(rows, 'Card •••• 2222')).toBe('Expires 01/2027');
      expect(value(rows, 'Card •••• 1111')).toBeUndefined();
    });

    it('stops at the sync lines for a provider with no bank accounts', () => {
      const rows = ConnectionDetailsUtils.institutionRows(
        institution({provider: 'ibkr', name: 'Interactive Brokers'}),
        []
      );

      expect(rows.map(row => row.label)).toEqual(['Provider', 'Status', 'Last synced']);
    });
  });

  describe('closedAccountRows', () => {
    const byId = (accounts: BankAccount[]) => new Map(accounts.map(a => [a.accountId, a]));

    it('words a delisted account', () => {
      const closed = account({
        closedAt: '2026-10-12T12:00:00Z',
        closedReason: 'absent_from_list',
        productType: 'black',
        lastSyncTimestamp: '2026-10-20T11:00:00Z',
        lastSyncDurationMs: 800,
        absentSince: '2026-10-10T12:00:00Z',
      });
      const rows = ConnectionDetailsUtils.closedAccountRows(closed, byId([closed]));

      expect(value(rows, 'Provider')).toBe('Monobank');
      expect(value(rows, 'Account')).toBe('Black · UAH');
      expect(value(rows, 'Card number')).toBe('•••• 1234');
      expect(value(rows, 'Status')).toBe('Closed Mon 12 Oct 2026');
      expect(value(rows, 'Reason')).toBe('No longer listed by Monobank');
      expect(value(rows, 'Last synced')).toBe('1h ago');
      expect(value(rows, 'Sync took')).toBe('800 ms');
      expect(value(rows, 'Missing from provider since')).toBe('Sat 10 Oct 2026');
      expect(value(rows, 'Replaced by')).toBeUndefined();
    });

    it('leaves out what the account never recorded', () => {
      const closed = account({closedAt: '2026-10-12T12:00:00Z'});
      const labels = ConnectionDetailsUtils.closedAccountRows(closed, byId([closed])).map(
        row => row.label
      );

      expect(labels).toEqual([
        'Provider',
        'Account',
        'Card number',
        'Status',
        'Reason',
        'Last synced',
      ]);
    });

    it('names the card expiry month', () => {
      const closed = account({closedAt: '2026-10-12T12:00:00Z', cardValidTo: '2026-09'});

      expect(
        value(ConnectionDetailsUtils.closedAccountRows(closed, byId([closed])), 'Card expires')
      ).toBe('09/2026');
    });

    it('names the live card that replaced a reissued one', () => {
      const closed = account({
        accountId: 'old',
        accountNumberLast4: '9312',
        closedAt: '2026-10-12T12:00:00Z',
        closedReason: 'reissued',
      });
      const successor = account({
        accountId: 'new',
        accountNumberLast4: '4455',
        predecessorAccountId: 'old',
      });
      const rows = ConnectionDetailsUtils.closedAccountRows(closed, byId([closed, successor]));

      expect(value(rows, 'Replaced by')).toBe('•••• 4455');
      expect(value(rows, 'Status')).toBe('Closed Mon 12 Oct 2026');
    });

    it('names the card this account replaced and when its number changed', () => {
      const closed = account({
        accountId: 'new',
        accountNumberLast4: '4455',
        closedAt: '2026-10-12T12:00:00Z',
        predecessorAccountId: 'old',
        panChangedAt: '2026-10-01T12:00:00Z',
      });
      const predecessor = account({accountId: 'old', accountNumberLast4: '9312'});
      const rows = ConnectionDetailsUtils.closedAccountRows(closed, byId([closed, predecessor]));

      expect(value(rows, 'Replaced card')).toBe('•••• 9312');
      expect(value(rows, 'Card number changed')).toBe('Thu 1 Oct 2026');
    });

    it('falls back to the previous last four on the same account', () => {
      const closed = account({closedAt: '2026-10-12T12:00:00Z', previousLast4: '7777'});

      expect(
        value(ConnectionDetailsUtils.closedAccountRows(closed, byId([closed])), 'Replaced card')
      ).toBe('•••• 7777');
    });
  });
});
