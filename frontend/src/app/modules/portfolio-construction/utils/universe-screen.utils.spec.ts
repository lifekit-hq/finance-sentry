import {describe, expect, it} from 'vitest';

import {UniverseScreenFixtures} from '../models/universe-screen/universe-screen.fixtures';
import {type UniverseRow} from '../models/universe-screen/universe-screen.model';
import {UniverseScreenUtils} from './universe-screen.utils';

const row = (symbol: string, overrides: Partial<UniverseRow> = {}): UniverseRow =>
  UniverseScreenFixtures.row({symbol, ...overrides});

const symbols = (rows: UniverseRow[]): string[] => rows.map(r => r.symbol);

describe('UniverseScreenUtils', () => {
  describe('filter', () => {
    const rows = [
      row('AAA'),
      row('BBB', {
        verdict: {word: 'Avoid', rulesVersion: 1, firedRuleId: 'weak_structure', firedRule: ''},
      }),
      UniverseScreenFixtures.failedRow('CCC'),
    ];

    it('keeps every row for All, including a name that failed to score', () => {
      expect(symbols(UniverseScreenUtils.filter(rows, 'All'))).toEqual(['AAA', 'BBB', 'CCC']);
    });

    it('keeps only the rows carrying the verdict word', () => {
      expect(symbols(UniverseScreenUtils.filter(rows, 'Avoid'))).toEqual(['BBB']);
    });

    it('returns nothing when no row carries the word', () => {
      expect(UniverseScreenUtils.filter(rows, 'StrongBuy')).toEqual([]);
    });
  });

  describe('sort', () => {
    it('orders the suggested book by construction rank, unranked names last, then ticker', () => {
      const rows = [
        row('ZZZ', {constructionRank: null}),
        row('BBB', {constructionRank: 2}),
        row('AAA', {constructionRank: 1}),
        row('MMM', {constructionRank: null}),
      ];

      expect(symbols(UniverseScreenUtils.sort(rows, 'construction'))).toEqual([
        'AAA',
        'BBB',
        'MMM',
        'ZZZ',
      ]);
    });

    it('orders by the raw structure score, highest first, and never by the adjusted one', () => {
      const rows = [
        row('LOW', {structure: {raw: 40, adjusted: 99, basis: 'adjusted', notEvaluable: []}}),
        row('HIGH', {structure: {raw: 90, adjusted: 10, basis: 'adjusted', notEvaluable: []}}),
        row('NONE', {structure: null}),
      ];

      expect(symbols(UniverseScreenUtils.sort(rows, 'structure'))).toEqual(['HIGH', 'LOW', 'NONE']);
    });

    it('orders by fundamentals, highest first, a missing score last', () => {
      const rows = [
        row('MID', {fundamentals: {score: 60, notEvaluable: []}}),
        row('GAP', {fundamentals: {score: null, notEvaluable: ['too_few_periods']}}),
        row('TOP', {fundamentals: {score: 80, notEvaluable: []}}),
      ];

      expect(symbols(UniverseScreenUtils.sort(rows, 'fundamentals'))).toEqual([
        'TOP',
        'MID',
        'GAP',
      ]);
    });

    it('orders crowding earliest first, an unclassified name last', () => {
      const rows = [
        row('EXT', {crowding: {class: 'Extended', extensionFromMa50: 0.2}}),
        row('UNK', {crowding: {class: null, extensionFromMa50: null}}),
        row('EAR', {crowding: {class: 'Early', extensionFromMa50: -0.02}}),
        row('NOR', {crowding: {class: 'Normal', extensionFromMa50: 0.04}}),
      ];

      expect(symbols(UniverseScreenUtils.sort(rows, 'crowding'))).toEqual([
        'EAR',
        'NOR',
        'EXT',
        'UNK',
      ]);
    });

    it('orders by the P/E premium to its own average, cheapest first, a missing premium last', () => {
      const valuation = (premiumPct: number | null): UniverseRow['valuation'] => ({
        notApplicable: false,
        trailingPe: 20,
        trailingPeFiveYearAvg: 22,
        premiumPct,
        impliedUpsidePct: null,
        notEvaluable: null,
        stale: false,
      });
      const rows = [
        row('RICH', {valuation: valuation(30)}),
        row('NONE', {valuation: valuation(null)}),
        row('CHEAP', {valuation: valuation(-12)}),
      ];

      expect(symbols(UniverseScreenUtils.sort(rows, 'valuation'))).toEqual([
        'CHEAP',
        'RICH',
        'NONE',
      ]);
    });

    it('orders by verdict, strongest first, a name that failed to score last', () => {
      const word = (w: 'StrongBuy' | 'Buy' | 'Wait' | 'Avoid'): UniverseRow['verdict'] => ({
        word: w,
        rulesVersion: 1,
        firedRuleId: 'x',
        firedRule: '',
      });
      const rows = [
        row('A', {verdict: word('Avoid')}),
        UniverseScreenFixtures.failedRow('F'),
        row('S', {verdict: word('StrongBuy')}),
        row('W', {verdict: word('Wait')}),
        row('B', {verdict: word('Buy')}),
      ];

      expect(symbols(UniverseScreenUtils.sort(rows, 'verdict'))).toEqual(['S', 'B', 'W', 'A', 'F']);
    });

    it('orders by ticker', () => {
      expect(
        symbols(UniverseScreenUtils.sort([row('MSF'), row('AAP'), row('KO')], 'symbol'))
      ).toEqual(['AAP', 'KO', 'MSF']);
    });

    it('does not change the list it was given', () => {
      const rows = [row('B'), row('A')];

      UniverseScreenUtils.sort(rows, 'symbol');

      expect(symbols(rows)).toEqual(['B', 'A']);
    });
  });

  describe('toView', () => {
    it('shows the adjusted structure the verdict reads with the raw one beside it', () => {
      const view = UniverseScreenUtils.toView(row('AAA'));

      expect(view.structureText).toBe('68');
      expect(view.structureRawText).toBe('raw 64');
    });

    it('hides the raw value when it equals the adjusted one', () => {
      const view = UniverseScreenUtils.toView(
        row('AAA', {structure: {raw: 70, adjusted: 70, basis: 'raw', notEvaluable: []}})
      );

      expect(view.structureRawText).toBe('');
    });

    it('reads a missing axis as no data, never as a neutral value', () => {
      const view = UniverseScreenUtils.toView(UniverseScreenFixtures.rowWithoutFundamentals());

      expect(view.fundamentalsText).toBe('no data');
      expect(view.notEvaluableTexts).toEqual(['too few periods']);
    });

    it('shows valuation as raw facts with the premium to the five-year average', () => {
      expect(UniverseScreenUtils.toView(row('AAA')).valuationText).toBe(
        'P/E 28 vs 5y avg 24 (+16.7%)'
      );
    });

    it('shows a discount without a plus sign', () => {
      const valuation = {
        notApplicable: false,
        trailingPe: 20,
        trailingPeFiveYearAvg: 25,
        premiumPct: -20,
        impliedUpsidePct: null,
        notEvaluable: null,
        stale: false,
      };

      expect(UniverseScreenUtils.toView(row('AAA', {valuation})).valuationText).toBe(
        'P/E 20 vs 5y avg 25 (-20%)'
      );
    });

    it('names why valuation could not be read', () => {
      const valuation = {
        notApplicable: false,
        trailingPe: 20,
        trailingPeFiveYearAvg: null,
        premiumPct: null,
        impliedUpsidePct: null,
        notEvaluable: 'no_five_year_average',
        stale: false,
      };

      expect(UniverseScreenUtils.toView(row('AAA', {valuation})).valuationText).toBe(
        'no five year average'
      );
    });

    it('marks a non-equity as not applicable', () => {
      const valuation = {
        notApplicable: true,
        trailingPe: null,
        trailingPeFiveYearAvg: null,
        premiumPct: null,
        impliedUpsidePct: null,
        notEvaluable: 'not_applicable',
        stale: false,
      };

      expect(UniverseScreenUtils.toView(row('AAA', {valuation})).valuationText).toBe(
        'not applicable'
      );
    });

    it('shows a name that failed to score as no data on every axis, with no weight', () => {
      const view = UniverseScreenUtils.toView(UniverseScreenFixtures.failedRow());

      expect([
        view.structureText,
        view.fundamentalsText,
        view.crowdingText,
        view.valuationText,
      ]).toEqual(['no data', 'no data', 'no data', 'no data']);
      expect(view.weightText).toBe('');
    });

    it('formats a weight with at most two decimals', () => {
      expect(UniverseScreenUtils.toView(row('AAA', {suggestedWeightPct: 16.66})).weightText).toBe(
        '16.66%'
      );
      expect(UniverseScreenUtils.toView(row('AAA', {suggestedWeightPct: 12})).weightText).toBe(
        '12%'
      );
    });
  });
});
