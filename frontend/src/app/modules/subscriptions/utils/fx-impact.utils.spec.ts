import {afterEach, describe, expect, it, vi} from 'vitest';

import {FX_IMPACT_ENABLED_STORAGE_KEY} from '../constants/fx-impact/fx-impact.constants';
import {type FxImpactPlan, type FxImpactTotals} from '../models/fx-impact/fx-impact.model';
import {FxImpactUtils} from './fx-impact.utils';

const PLAN: FxImpactPlan = {
  id: 'p1',
  merchant: 'Mortgage',
  kind: 'installment',
  currency: 'UAH',
  paymentNative: 14060.96,
  signingDate: '2024-05-01',
  signingDateIsObserved: false,
  signingUnitsPerBase: 39.52,
  currentUnitsPerBase: 44.54,
  paymentCostAtSigning: 355.84,
  paymentCostNow: 315.68,
  costChangePercent: -11.29,
  paymentsMade: 28,
  paymentsRemaining: 20,
  remainingCostAtSigningRate: 7116.8,
  remainingCostAtTodayRate: 6313.6,
  remainingSaving: 803.2,
  paidSaving: 321.4,
  rates: [
    {date: '2024-05-01', unitsPerBase: 39.52},
    {date: '2026-11-05', unitsPerBase: 44.54},
  ],
};

const totals = (overrides: Partial<FxImpactTotals>): FxImpactTotals => ({
  paidSaving: 0,
  remainingSaving: 0,
  totalSaving: 0,
  hasUnknownRemainder: false,
  ...overrides,
});

describe('FxImpactUtils.readEnabled / writeEnabled', () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('is on when this device never chose', () => {
    expect(FxImpactUtils.readEnabled()).toBe(true);
  });

  it('remembers an off and an on choice', () => {
    FxImpactUtils.writeEnabled(false);
    expect(localStorage.getItem(FX_IMPACT_ENABLED_STORAGE_KEY)).toBe('0');
    expect(FxImpactUtils.readEnabled()).toBe(false);

    FxImpactUtils.writeEnabled(true);
    expect(FxImpactUtils.readEnabled()).toBe(true);
  });

  it('falls back to on when storage cannot be read', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(FxImpactUtils.readEnabled()).toBe(true);
  });

  it('survives storage that cannot be written', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(() => FxImpactUtils.writeEnabled(false)).not.toThrow();
  });
});

describe('FxImpactUtils.planView', () => {
  it('turns the rate history into time-placed chart points', () => {
    const {chart} = FxImpactUtils.planView(PLAN);

    expect(chart).toEqual([
      {label: '2024-05-01', value: 39.52, time: new Date('2024-05-01').getTime()},
      {label: '2026-11-05', value: 44.54, time: new Date('2026-11-05').getTime()},
    ]);
  });

  it('reads a positive saving as saved, with the amount', () => {
    const view = FxImpactUtils.planView(PLAN);

    expect(view.paidSaved).toBe(true);
    expect(view.paidAmount).toBe(321.4);
    expect(view.remainingSaved).toBe(true);
    expect(view.remainingAmount).toBe(803.2);
  });

  it('reads a negative saving as extra cost, with the absolute amount', () => {
    const view = FxImpactUtils.planView({...PLAN, paidSaving: -50, remainingSaving: -120});

    expect(view.paidSaved).toBe(false);
    expect(view.paidAmount).toBe(50);
    expect(view.remainingSaved).toBe(false);
    expect(view.remainingAmount).toBe(120);
  });

  it('has no remaining amount when the remainder is unknown', () => {
    const view = FxImpactUtils.planView({...PLAN, remainingSaving: null});

    expect(view.remainingAmount).toBeNull();
  });
});

describe('FxImpactUtils.summaryLine', () => {
  it('says what was saved so far and what is still to save', () => {
    expect(
      FxImpactUtils.summaryLine(totals({paidSaving: 321.4, remainingSaving: 803.2}), 'EUR')
    ).toBe('Rate moves have saved you €321.40 so far and will save €803.20 on the payments left.');
  });

  it('says what was cost and will cost when the rate went against the payer', () => {
    expect(FxImpactUtils.summaryLine(totals({paidSaving: -10, remainingSaving: -5}), 'USD')).toBe(
      'Rate moves have cost you $10.00 more so far and will cost $5.00 more on the payments left.'
    );
  });

  it('leaves out a part that did not move', () => {
    expect(FxImpactUtils.summaryLine(totals({paidSaving: 40}), 'USD')).toBe(
      'Rate moves have saved you $40.00 so far.'
    );
    expect(FxImpactUtils.summaryLine(totals({remainingSaving: 40}), 'USD')).toBe(
      "Rate moves have not changed what you've paid so far and will save $40.00 on the payments left."
    );
  });

  it('says nothing changed when both parts are flat', () => {
    expect(FxImpactUtils.summaryLine(totals({}), 'USD')).toBe(
      'Exchange-rate moves have not changed what these plans cost you.'
    );
  });
});
