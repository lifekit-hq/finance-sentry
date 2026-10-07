import {describe, expect, it} from 'vitest';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';
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
    expect(pipe.transform(account({}), 'USD')).toEqual({
      native: '€1,200.00',
      equivalent: '~ $1,300',
      owed: false,
    });
  });

  it('labels the equivalent with the base currency, not dollars', () => {
    const result = pipe.transform(
      account({currency: 'UAH', currentBalance: 40000, balanceInBaseCurrency: 900}),
      'EUR',
    );

    expect(result.equivalent).toBe('~ €900');
  });

  it('shows the equivalent of a USD account when the base currency is not USD', () => {
    const result = pipe.transform(
      account({currency: 'USD', currentBalance: 1000, balanceInBaseCurrency: 920}),
      'EUR',
    );

    expect(result.equivalent).toBe('~ €920');
  });

  it('omits the equivalent for an account already in the base currency', () => {
    const result = pipe.transform(
      account({currency: 'EUR', currentBalance: 1200, balanceInBaseCurrency: 1199.99}),
      'EUR',
    );

    expect(result).toEqual({native: '€1,200.00', equivalent: null, owed: false});
  });

  it('omits the equivalent when no conversion exists', () => {
    expect(pipe.transform(account({balanceInBaseCurrency: null}), 'USD').equivalent).toBeNull();
  });

  it('labels a credit account balance as owed, keeping the stored positive amount', () => {
    const result = pipe.transform(account({accountType: 'credit', currentBalance: 120}), 'USD');

    expect(result.native).toBe('Owes €120.00');
    expect(result.owed).toBe(true);
  });

  it.each([0, -5])('does not label a credit account with balance %d as owed', currentBalance => {
    const result = pipe.transform(account({accountType: 'credit', currentBalance}), 'USD');

    expect(result.native).toBe(MoneyUtils.format(currentBalance, 'EUR'));
    expect(result.owed).toBe(false);
  });
});
