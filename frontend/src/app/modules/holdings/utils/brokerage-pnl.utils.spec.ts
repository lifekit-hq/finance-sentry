import {describe, expect, it} from 'vitest';

import {
  type BrokeragePnl,
  type PnlFigure,
  type PnlRow,
} from '../models/brokerage-pnl/brokerage-pnl.model';
import {BrokeragePnlUtils} from './brokerage-pnl.utils';

const figure = (overrides: Partial<PnlFigure> = {}): PnlFigure => ({
  usd: 150,
  percentOfNetDeposited: 10,
  status: 'COMPLETE',
  measured: 2,
  total: 2,
  coverage: null,
  unavailableSymbols: [],
  ...overrides,
});

const pnl = (overrides: Partial<BrokeragePnl> = {}): BrokeragePnl => ({
  from: '2026-01-01',
  to: '2026-10-01',
  netDepositedUsd: 1500,
  sourcesWithoutFlows: [],
  realized: figure(),
  unrealized: figure({usd: 300, percentOfNetDeposited: 20, measured: 3, total: 3}),
  currency: 'USD',
  ...overrides,
});

const row = (data: BrokeragePnl, key: string): PnlRow => {
  const found = BrokeragePnlUtils.rows(data).find(r => r.key === key);
  if (!found) {
    throw new Error(`no ${key} row`);
  }
  return found;
};

describe('BrokeragePnlUtils.rows', () => {
  it('lists capital in the window, realized and unrealized in reading order', () => {
    expect(BrokeragePnlUtils.rows(pnl()).map(r => r.key)).toEqual([
      'net-deposited',
      'realized',
      'unrealized',
    ]);
  });

  it('puts a complete gain over the capital in the window with its sign and tone', () => {
    const realized = row(pnl(), 'realized');

    expect(realized.amount).toBe(150);
    expect(realized.percentLabel).toBe('+10.0%');
    expect(realized.tone).toBe('gain');
    expect(realized.unavailable).toBeNull();
    expect(realized.note).toBe('From your fills');
    expect(realized.detail).toBeNull();
  });

  it('reads a loss as a loss', () => {
    const loss = row(pnl({realized: figure({usd: -50, percentOfNetDeposited: -3.3})}), 'realized');

    expect(loss.tone).toBe('loss');
    expect(loss.percentLabel).toBe('-3.3%');
  });

  it('reads a real zero as neutral, not as missing', () => {
    const none = row(
      pnl({realized: figure({usd: 0, percentOfNetDeposited: 0, measured: 0, total: 0})}),
      'realized'
    );

    expect(none.amount).toBe(0);
    expect(none.tone).toBe('neutral');
    expect(none.unavailable).toBeNull();
    expect(none.note).toBe('Nothing sold yet');
  });

  it('keeps a partial realized amount, drops its percent and names what is left out', () => {
    const partial = row(
      pnl({
        realized: figure({
          status: 'PARTIAL',
          percentOfNetDeposited: null,
          measured: 1,
          total: 5,
          unavailableSymbols: ['AAA', 'BBB', 'CCC', 'DDD'],
        }),
      }),
      'realized'
    );

    expect(partial.amount).toBe(150);
    expect(partial.percentLabel).toBeNull();
    expect(partial.note).toBe('Partial: 1 of 5 sold');
    expect(partial.detail).toBe(
      'Realized P&L leaves out AAA, BBB, CCC and 1 more: your fills do not cover their history.'
    );
  });

  it('withholds realized when the fills do not cover the history of what sold', () => {
    const unavailable = row(
      pnl({
        realized: figure({
          status: 'UNAVAILABLE',
          usd: null,
          percentOfNetDeposited: null,
          measured: 0,
          total: 1,
          unavailableSymbols: ['OLD'],
        }),
      }),
      'realized'
    );

    expect(unavailable.amount).toBeNull();
    expect(unavailable.unavailable).toBe('Basis unavailable');
    expect(unavailable.note).toBe('Fills incomplete');
    expect(unavailable.detail).toBe(
      'Realized P&L is not stated: your fills do not cover the history of OLD.'
    );
  });

  it('says no fills are ingested when realized has nothing to name', () => {
    const none = row(
      pnl({
        realized: figure({status: 'UNAVAILABLE', usd: null, percentOfNetDeposited: null, total: 0}),
      }),
      'realized'
    );

    expect(none.note).toBe('No fills yet');
    expect(none.detail).toBeNull();
  });

  it('labels a complete unrealized figure as verified against the fills', () => {
    const unrealized = row(pnl(), 'unrealized');

    expect(unrealized.percentLabel).toBe('+20.0%');
    expect(unrealized.note).toBe('Cost verified from fills');
  });

  it('keeps a partial unrealized subset with its coverage and no percent', () => {
    const partial = row(
      pnl({
        unrealized: figure({
          usd: 100,
          status: 'PARTIAL',
          percentOfNetDeposited: null,
          measured: 1,
          total: 3,
          coverage: 0.4,
          unavailableSymbols: ['BBB', 'CCC'],
        }),
      }),
      'unrealized'
    );

    expect(partial.amount).toBe(100);
    expect(partial.percentLabel).toBeNull();
    expect(partial.note).toBe('Partial: 1 of 3 · 40% of value');
    expect(partial.detail).toBe(
      'Unrealized P&L covers only the positions whose cost your fills verify; BBB, CCC are left out.'
    );
  });

  it('leaves the coverage share out of a partial note when none is known', () => {
    const partial = row(
      pnl({
        unrealized: figure({
          status: 'PARTIAL',
          percentOfNetDeposited: null,
          measured: 1,
          total: 1,
          coverage: null,
          unavailableSymbols: ['BBB'],
        }),
      }),
      'unrealized'
    );

    expect(partial.note).toBe('Partial: 1 of 1');
    expect(partial.detail).toContain('BBB is left out');
  });

  it('shows "Basis unavailable" instead of a number when no position is verified', () => {
    const unavailable = row(
      pnl({
        unrealized: figure({
          status: 'UNAVAILABLE',
          usd: null,
          percentOfNetDeposited: null,
          measured: 0,
        }),
      }),
      'unrealized'
    );

    expect(unavailable.amount).toBeNull();
    expect(unavailable.unavailable).toBe('Basis unavailable');
    expect(unavailable.note).toBe('No verified cost basis');
    expect(unavailable.detail).toBe(
      'Unrealized P&L is not stated: no position has a cost basis your fills verify.'
    );
  });

  it('shows the capital in the window when it is known', () => {
    const deposited = row(pnl(), 'net-deposited');

    expect(deposited.amount).toBe(1500);
    expect(deposited.signed).toBe(false);
    expect(deposited.unavailable).toBeNull();
    expect(deposited.label).toBe('Capital in the window');
    expect(deposited.note).toBe('Opening value + deposits less withdrawals');
    expect(deposited.detail).toBe(
      'Capital in the window is the opening value, plus providers linked in the window, plus deposits less withdrawals. When the history opens after the account was funded, the opening value already includes earlier market gains.'
    );
  });

  it('names the provider whose deposits are unknown instead of showing a number', () => {
    const deposited = row(
      pnl({netDepositedUsd: null, sourcesWithoutFlows: ['ibkr']}),
      'net-deposited'
    );

    expect(deposited.amount).toBeNull();
    expect(deposited.unavailable).toBe('Not measured');
    expect(deposited.note).toBe('Not measured yet');
    expect(deposited.detail).toBe(
      'Capital in the window: no deposits or withdrawals recorded yet for Interactive Brokers, so none is stated.'
    );
  });

  it('says it needs history when nothing names a provider', () => {
    const deposited = row(pnl({netDepositedUsd: null}), 'net-deposited');

    expect(deposited.note).toBe('Not measured yet');
    expect(deposited.detail).toBeNull();
  });
});
