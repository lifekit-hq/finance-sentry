import {describe, expect, it} from 'vitest';

import {type ConcentrationHistory} from '../models/risk-picture/risk-picture.model';
import {ConcentrationHistoryUtils} from './concentration-history.utils';

const history = (overrides: Partial<ConcentrationHistory>): ConcentrationHistory => ({
  points: [],
  cryptoTarget: null,
  cashTarget: null,
  reason: null,
  ...overrides,
});

describe('ConcentrationHistoryUtils.view', () => {
  it('reads a drifting series as percentage points with the drift from the IPS target', () => {
    const view = ConcentrationHistoryUtils.view(
      history({
        points: [
          {date: '2026-01-05', top3Share: 0.8, cryptoShare: 0.1, cashShare: 0.1},
          {date: '2026-06-05', top3Share: 0.85, cryptoShare: 0.15, cashShare: 0.3},
        ],
        cryptoTarget: 0.1,
      }),
      'crypto'
    );

    expect(view?.chart.map(p => p.value)).toEqual([10, 15]);
    expect(view?.summary).toBe('Now 15.0%, target 10.0%, drift +5.0 pts');
    expect(view?.target.map(p => p.value)).toEqual([10, 10]);
    expect(view?.target.map(p => p.label)).toEqual(['2026-01-05', '2026-06-05']);
    expect(view?.target.map(p => p.time)).toEqual(view?.chart.map(p => p.time));
    expect(view?.domain).toEqual({min: 9, max: 16});
  });

  it('shows a flat series as the same value each day with no drift', () => {
    const flat = history({
      points: [
        {date: '2026-01-05', top3Share: 0.9, cryptoShare: 0.1, cashShare: null},
        {date: '2026-02-05', top3Share: 0.9, cryptoShare: 0.1, cashShare: null},
        {date: '2026-03-05', top3Share: 0.9, cryptoShare: 0.1, cashShare: null},
      ],
      cryptoTarget: 0.1,
    });

    const view = ConcentrationHistoryUtils.view(flat, 'crypto');

    expect(new Set(view?.chart.map(p => p.value))).toEqual(new Set([10]));
    expect(view?.summary).toBe('Now 10.0%, target 10.0%, drift 0.0 pts');
    expect(view?.target.map(p => p.value)).toEqual([10, 10, 10]);
    expect(view?.domain).toEqual({min: 9, max: 11});
  });

  it('draws the cash target as its own reference line from the cash target, not the crypto one', () => {
    const view = ConcentrationHistoryUtils.view(
      history({
        points: [
          {date: '2026-01-05', top3Share: 0.8, cryptoShare: 0.1, cashShare: 0.1},
          {date: '2026-06-05', top3Share: 0.8, cryptoShare: 0.1, cashShare: 0.3},
        ],
        cryptoTarget: 0.1,
        cashTarget: 0.05,
      }),
      'cash'
    );

    expect(view?.target.map(p => p.value)).toEqual([5, 5]);
  });

  it('has no target to compare the top-three share against and says only where it stands', () => {
    const view = ConcentrationHistoryUtils.view(
      history({
        points: [
          {date: '2026-01-05', top3Share: 0.75, cryptoShare: 0, cashShare: null},
          {date: '2026-02-05', top3Share: 0.9, cryptoShare: 0, cashShare: null},
        ],
      }),
      'top3'
    );

    expect(view?.summary).toBe('Now 90.0%');
    expect(view?.target).toEqual([]);
  });

  it('skips days with no measure and returns null when none has one', () => {
    const points = [
      {date: '2026-01-05', top3Share: 0.8, cryptoShare: 0.1, cashShare: null},
      {date: '2026-02-05', top3Share: 0.8, cryptoShare: 0.1, cashShare: null},
    ];

    expect(ConcentrationHistoryUtils.view(history({points}), 'cash')).toBeNull();
  });

  it('draws nothing when the server reports too little history', () => {
    expect(
      ConcentrationHistoryUtils.view(history({reason: 'NotEnoughHistory'}), 'top3')
    ).toBeNull();
  });
});
