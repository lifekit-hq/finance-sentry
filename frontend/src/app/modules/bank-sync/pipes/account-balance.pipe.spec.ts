import {describe, expect, it} from 'vitest';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {AccountBalancePipe} from './account-balance.pipe';

function account(overrides: Partial<AccountBalanceItem>): AccountBalanceItem {
  return {
    accountId: 'acc-1',
    bankName: 'Test Bank',
    accountType: 'current',
    accountNumberLast4: '1234',
    currency: 'EUR',
    provider: 'truelayer',
    category: 'banking',
    currentBalance: 1200,
    balanceInBaseCurrency: 1300,
    syncStatus: 'synced',
    lastSyncTimestamp: null,
    ...overrides,
  };
}

describe('AccountBalancePipe', () => {
  const pipe = new AccountBalancePipe();

  it('shows the native amount first with a muted base equivalent', () => {
    expect(pipe.transform(account({}))).toEqual({
      native: '€1,200.00',
      usd: '~ $1,300',
      owed: false,
    });
  });

  it('omits the equivalent for a USD account', () => {
    const result = pipe.transform(account({currency: 'USD', balanceInBaseCurrency: 1200}));

    expect(result).toEqual({native: '$1,200.00', usd: null, owed: false});
  });

  it('omits the equivalent when no conversion exists', () => {
    expect(pipe.transform(account({balanceInBaseCurrency: null})).usd).toBeNull();
  });

  it('labels a credit account balance as owed, keeping the stored positive amount', () => {
    const result = pipe.transform(account({accountType: 'credit', currentBalance: 120}));

    expect(result.native).toBe('Owes €120.00');
    expect(result.owed).toBe(true);
  });
});
