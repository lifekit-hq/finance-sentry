import {describe, expect, it} from 'vitest';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {PositionUtils} from './position.utils';

function position(overrides: Partial<AccountBalanceItem>): AccountBalanceItem {
  return {
    accountId: 'p1',
    bankName: 'Interactive Brokers',
    accountType: 'brokerage',
    accountNumberLast4: 'GOOG',
    currency: 'GOOGL',
    symbol: 'GOOGL',
    provider: 'ibkr',
    category: 'brokerage',
    currentBalance: 40,
    balanceInBaseCurrency: 6800.5,
    syncStatus: 'synced',
    lastSyncTimestamp: null,
    ...overrides,
  };
}

describe('PositionUtils.value', () => {
  it('formats the base-currency value', () => {
    expect(PositionUtils.value(position({}), 'USD')).toBe('$6,800.50');
  });

  it('falls back to the native value when there is no base value', () => {
    expect(
      PositionUtils.value(
        position({balanceInBaseCurrency: null, nativeValue: 1200, nativeCurrency: 'EUR'}),
        'USD'
      )
    ).toBe('€1,200.00');
  });

  it('renders a dash when no value is known', () => {
    expect(PositionUtils.value(position({balanceInBaseCurrency: null}), 'USD')).toBe('—');
  });
});

describe('PositionUtils.quantity', () => {
  it('counts brokerage positions in shares', () => {
    expect(PositionUtils.quantity(position({}))).toBe('40 shares');
    expect(PositionUtils.quantity(position({currentBalance: 1}))).toBe('1 share');
  });

  it('counts crypto in its ticker', () => {
    expect(
      PositionUtils.quantity(
        position({category: 'crypto', provider: 'binance', symbol: 'BTC', currentBalance: 0.0812})
      )
    ).toBe('0.0812 BTC');
  });

  it('counts other providers in units', () => {
    expect(PositionUtils.quantity(position({provider: 'inzhur', currentBalance: 3}))).toBe(
      '3 units'
    );
  });
});
