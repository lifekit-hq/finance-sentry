import {describe, expect, it} from 'vitest';

import {CATEGORY_COLOUR} from '../../../modules/bank-sync/store/accounts/accounts.computed';
import {SLEEVE_COLOUR} from '../../../modules/bank-sync/store/dashboard/dashboard.computed';
import {ASSET_CLASS_COLOUR} from '../../../modules/holdings/store/holdings.computed';
import {
  CASH_COLOUR,
  CRYPTO_COLOUR,
  EQUITY_COLOUR,
  FLOW_IN_COLOUR,
  FLOW_OUT_COLOUR,
} from './chart-colour.constants';

describe('fixed meaning colours', () => {
  it('pins the asset-class tokens', () => {
    expect(EQUITY_COLOUR).toEqual({fixed: 'asset-equity'});
    expect(CRYPTO_COLOUR).toEqual({fixed: 'asset-crypto'});
    expect(CASH_COLOUR).toEqual({fixed: 'asset-cash'});
  });

  it('pins the money in / out tokens', () => {
    expect(FLOW_IN_COLOUR).toEqual({fixed: 'flow-in'});
    expect(FLOW_OUT_COLOUR).toEqual({fixed: 'flow-out'});
  });

  it('colours holdings by asset class: crypto orange, cash green, equity the equity token', () => {
    expect(ASSET_CLASS_COLOUR.crypto).toBe(CRYPTO_COLOUR);
    expect(ASSET_CLASS_COLOUR.cash).toBe(CASH_COLOUR);
    expect(ASSET_CLASS_COLOUR.equity).toBe(EQUITY_COLOUR);
  });

  it('leaves bonds and real estate on the theme series', () => {
    expect(ASSET_CLASS_COLOUR.bonds).toHaveProperty('step');
    expect(ASSET_CLASS_COLOUR.realEstate).toHaveProperty('step');
  });

  it('colours account categories like the asset class they hold', () => {
    expect(CATEGORY_COLOUR.banking).toBe(CASH_COLOUR);
    expect(CATEGORY_COLOUR.brokerage).toBe(EQUITY_COLOUR);
    expect(CATEGORY_COLOUR.crypto).toBe(CRYPTO_COLOUR);
    expect(CATEGORY_COLOUR.other).toHaveProperty('step');
  });

  it('colours net-worth sleeves like the asset class they hold', () => {
    expect(SLEEVE_COLOUR.banking).toBe(CASH_COLOUR);
    expect(SLEEVE_COLOUR.brokerage).toBe(EQUITY_COLOUR);
    expect(SLEEVE_COLOUR.crypto).toBe(CRYPTO_COLOUR);
  });
});
