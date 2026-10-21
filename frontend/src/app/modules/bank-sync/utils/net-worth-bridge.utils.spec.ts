import {describe, expect, it} from 'vitest';

import {type NetWorthBridgeResponse} from '../models/net-worth-bridge/net-worth-bridge.model';
import {NetWorthBridgeUtils} from './net-worth-bridge.utils';

const bridge = (overrides: Partial<NetWorthBridgeResponse> = {}): NetWorthBridgeResponse => ({
  from: '2026-09-01',
  to: '2026-10-01',
  startValue: 10_000,
  endValue: 11_000,
  savings: 600,
  investmentReturn: 300,
  market: 300,
  income: 0,
  fees: 0,
  taxes: 0,
  fxTranslation: 100,
  revaluation: 0,
  newlyLinked: 0,
  unlinked: 0,
  residual: 0,
  residualAboveTolerance: false,
  quality: 'COMPLETE',
  reasons: [],
  currency: 'USD',
  ...overrides,
});

const unavailable = (): NetWorthBridgeResponse =>
  bridge({
    from: null,
    to: null,
    startValue: null,
    endValue: null,
    savings: null,
    investmentReturn: null,
    market: null,
    income: null,
    fees: null,
    taxes: null,
    fxTranslation: null,
    revaluation: null,
    newlyLinked: null,
    unlinked: null,
    residual: null,
    quality: 'UNAVAILABLE',
    reasons: [{code: 'SHORT_HISTORY'}],
  });

describe('NetWorthBridgeUtils', () => {
  describe('headline', () => {
    it('reads End − Start with the bridge quality and reasons', () => {
      const reasons = [{code: 'RESIDUAL_ABOVE_TOLERANCE'}];

      expect(NetWorthBridgeUtils.headline(bridge({quality: 'DEGRADED', reasons}))).toEqual({
        amount: 1_000,
        percent: null,
        method: 'BRIDGE',
        scope: 'umbrella',
        window: {from: '2026-09-01', to: '2026-10-01'},
        quality: 'DEGRADED',
        reasons,
        currency: 'USD',
      });
    });

    it('has no amount and no closing date when the bridge is unavailable', () => {
      const headline = NetWorthBridgeUtils.headline(unavailable());

      expect(headline.amount).toBeNull();
      expect(headline.window).toEqual({from: null, to: ''});
    });
  });

  describe('steps', () => {
    it('is empty when the bridge has no figures', () => {
      expect(NetWorthBridgeUtils.steps(unavailable())).toEqual([]);
    });

    it('draws the start, the three standing lines, the residual and the end', () => {
      const steps = NetWorthBridgeUtils.steps(bridge());

      expect(steps.map(s => [s.key, s.label, s.amount, s.kind])).toEqual([
        ['start', 'Start', 10_000, 'total'],
        ['savings', 'Savings', 600, 'gain'],
        ['investmentReturn', 'Investment return', 300, 'gain'],
        ['fxTranslation', 'Currency moves', 100, 'gain'],
        ['residual', 'Unexplained', 0, 'residual'],
        ['end', 'End', 11_000, 'total'],
      ]);
    });

    it('runs each bar from the total before it to the total after it on one padded scale', () => {
      const steps = NetWorthBridgeUtils.steps(bridge());
      // 10,000 → 11,000 padded by 5% of the span either side: 9,950 → 11,050.
      const at = (value: number): number => ((value - 9_950) / 1_100) * 100;

      expect(steps[0].offset).toBeCloseTo(at(10_000));
      expect(steps[0].width).toBe(0);
      expect(steps[1].offset).toBeCloseTo(at(10_000));
      expect(steps[1].width).toBeCloseTo(at(10_600) - at(10_000));
      expect(steps[2].offset).toBeCloseTo(at(10_600));
      expect(steps[3].offset).toBeCloseTo(at(10_900));
      expect(steps[5].offset).toBeCloseTo(at(11_000));
    });

    it('starts a loss bar at the lower total it falls to', () => {
      const steps = NetWorthBridgeUtils.steps(
        bridge({endValue: 9_500, savings: -500, investmentReturn: 0, market: 0, fxTranslation: 0})
      );
      const savings = steps.find(s => s.key === 'savings');

      expect(savings?.kind).toBe('loss');
      expect(savings?.offset).toBeCloseTo(steps[steps.length - 1].offset);
      expect(savings?.width).toBeGreaterThan(0);
    });

    it('marks a standing line that moved nothing as flat and keeps it', () => {
      const steps = NetWorthBridgeUtils.steps(bridge({fxTranslation: 0, endValue: 10_900}));
      const fx = steps.find(s => s.key === 'fxTranslation');

      expect(fx?.kind).toBe('flat');
      expect(fx?.width).toBe(0);
    });

    it('adds revaluation and the link lines only when they moved the total, unlinked as a loss', () => {
      const steps = NetWorthBridgeUtils.steps(
        bridge({revaluation: 200, newlyLinked: 1_500, unlinked: 700, endValue: 12_000})
      );

      expect(steps.map(s => s.key)).toEqual([
        'start',
        'savings',
        'investmentReturn',
        'fxTranslation',
        'revaluation',
        'newlyLinked',
        'unlinked',
        'residual',
        'end',
      ]);
      expect(steps.find(s => s.key === 'unlinked')).toMatchObject({amount: -700, kind: 'loss'});
    });

    it('always keeps the residual, whatever its sign', () => {
      const steps = NetWorthBridgeUtils.steps(bridge({residual: -250, endValue: 10_750}));

      expect(steps.find(s => s.key === 'residual')).toMatchObject({amount: -250, kind: 'residual'});
    });

    it('reads a missing line as zero', () => {
      const steps = NetWorthBridgeUtils.steps(
        bridge({savings: null, endValue: 10_400, residual: null, unlinked: null})
      );

      expect(steps.find(s => s.key === 'savings')?.amount).toBe(0);
      expect(steps.find(s => s.key === 'residual')?.amount).toBe(0);
      expect(steps.some(s => s.key === 'unlinked')).toBe(false);
    });

    it('puts every row in the middle when nothing moved', () => {
      const steps = NetWorthBridgeUtils.steps(
        bridge({endValue: 10_000, savings: 0, investmentReturn: 0, market: 0, fxTranslation: 0})
      );

      expect(steps.every(s => s.offset === 50 && s.width === 0)).toBe(true);
    });

    it('names the parts of the return when income, fees or taxes moved it', () => {
      const steps = NetWorthBridgeUtils.steps(
        bridge({investmentReturn: 300, market: 290, income: 40, fees: 12, taxes: 18})
      );

      expect(steps.find(s => s.key === 'investmentReturn')?.detail).toBe(
        'market +$290.00 · income +$40.00 · fees -$12.00 · taxes -$18.00'
      );
    });

    it('names no parts when the market is all of the return', () => {
      const steps = NetWorthBridgeUtils.steps(bridge({income: null, fees: null, taxes: null}));

      expect(steps.find(s => s.key === 'investmentReturn')?.detail).toBeNull();
    });

    it('reads a missing market as zero beside the parts that moved', () => {
      const steps = NetWorthBridgeUtils.steps(bridge({market: null, income: 300}));

      expect(steps.find(s => s.key === 'investmentReturn')?.detail).toBe(
        'market $0.00 · income +$300.00'
      );
    });
  });

  describe('summary', () => {
    it('is null when the bridge has no figures', () => {
      expect(NetWorthBridgeUtils.summary(unavailable())).toBeNull();
    });

    it('reads three lines and the change, with no note when they add up', () => {
      const summary = NetWorthBridgeUtils.summary(bridge());

      expect(summary?.lines.map(l => [l.key, l.label, l.amount])).toEqual([
        ['savings', 'You saved', 600],
        ['investmentReturn', 'Markets', 300],
        ['fxTranslation', 'Exchange rates', 100],
      ]);
      expect(summary?.change).toBe(1_000);
      expect(summary?.changeNote).toBeNull();
      expect(summary?.currency).toBe('USD');
    });

    it('folds revaluation, the link lines and the residual into the change note', () => {
      const summary = NetWorthBridgeUtils.summary(
        bridge({
          endValue: 11_350,
          revaluation: 150,
          newlyLinked: 400,
          unlinked: -100,
          residual: -100,
        })
      );

      expect(summary?.change).toBe(1_350);
      expect(summary?.changeNote).toBe(
        'Includes +$350 from accounts linked or removed, revaluations and moves not yet explained.'
      );
    });

    it('leaves a move under one unit out of the note', () => {
      expect(NetWorthBridgeUtils.summary(bridge({endValue: 11_000.4}))?.changeNote).toBeNull();
    });

    it('reads a missing line as zero', () => {
      const summary = NetWorthBridgeUtils.summary(bridge({savings: null, endValue: 10_400}));

      expect(summary?.lines[0].amount).toBe(0);
      expect(summary?.changeNote).toBeNull();
    });

    it('explains a rate move in one sentence, by direction', () => {
      const sentence = (fxTranslation: number) =>
        NetWorthBridgeUtils.summary(bridge({fxTranslation, endValue: 10_900 + fxTranslation}))
          ?.lines[2].explanation;

      expect(sentence(100)).toBe(
        'Rates moved, so your accounts in other currencies are worth more in USD, though you did nothing.'
      );
      expect(sentence(-100)).toBe(
        'Rates moved, so your accounts in other currencies are worth less in USD, though you did nothing.'
      );
      expect(sentence(0.2)).toBe(
        'Accounts in other currencies are counted in USD; rates did not move them.'
      );
    });
  });
});
