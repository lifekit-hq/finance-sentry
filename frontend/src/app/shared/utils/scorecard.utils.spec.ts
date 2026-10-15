import {describe, expect, it} from 'vitest';

import {
  SCORECARD_MISSING,
  SCORECARD_NOT_APPLICABLE,
  SCORECARD_NOT_SCORED,
  SCORECARD_STALE_NOTE,
  SCORECARD_UNKNOWN,
} from '../constants/scorecard/scorecard.constants';
import {ScorecardFixtures} from '../models/scorecard/scorecard.fixtures';
import {type ScorecardAxisView, type ScorecardDto} from '../models/scorecard/scorecard.model';
import {ScorecardUtils} from './scorecard.utils';

const card = ScorecardFixtures.scorecard();

function axis(scorecard: ScorecardDto, name: ScorecardAxisView['axis']): ScorecardAxisView {
  const found = ScorecardUtils.axes(scorecard).find(a => a.axis === name);
  if (!found) {
    throw new Error(`no ${name} axis`);
  }
  return found;
}

function facts(view: ScorecardAxisView): Record<string, string> {
  return Object.fromEntries(view.facts.map(f => [f.label, f.value]));
}

function lines(view: ScorecardAxisView): string[] {
  return view.facts.map(f => `${f.label}: ${f.value}`);
}

describe('ScorecardUtils.axes', () => {
  it('returns the four axes in their fixed order', () => {
    expect(ScorecardUtils.axes(card).map(a => a.axis)).toEqual([
      'Structure',
      'Fundamentals',
      'Crowding',
      'Valuation',
    ]);
  });

  it('reads the adjusted structure score beside the raw one, with every fact behind it', () => {
    const structure = axis(card, 'Structure');

    expect(structure.summary).toBe('88 / 100 (raw 92)');
    expect(lines(structure)).toEqual([
      'Raw score: 92',
      'Regime adjustment: -4 pts',
      'Regime: volatility elevated, rates rising',
      'Return 21d: +11.0%',
      'Relative strength 21d: +7.0%',
      'Relative strength 63d: +18.0%',
      'Extension from 50-day MA: +24.0%',
      'Volume vs average: 1.80x',
      'Today z-score: 1.23',
      'Sector rank: #2 (+3)',
      'From 63-day high: -2.0%',
    ]);
    expect(structure.notes).toEqual(['risk off haircut']);
  });

  it('sorts the windows by trading days and drops a window with no value', () => {
    const labels = axis(card, 'Structure').facts.map(f => f.label);

    expect(labels).not.toContain('Return 63d');
    expect(labels.indexOf('Relative strength 21d')).toBeLessThan(
      labels.indexOf('Relative strength 63d')
    );
  });

  it('shows no raw suffix and no adjustment when the regime moved nothing', () => {
    const structure = axis(
      ScorecardFixtures.scorecard({
        structure: {
          ...card.structure,
          rawScore: 75,
          adjustedScore: 75,
          adjustmentPoints: 0,
          volatilityRegime: null,
          ratesRegime: null,
          sectorRankDelta: null,
          regimeRationale: ['no_regime_data'],
        },
      }),
      'Structure'
    );

    expect(structure.summary).toBe('75 / 100');
    expect(facts(structure)['Regime adjustment']).toBeUndefined();
    expect(facts(structure)['Regime']).toBeUndefined();
    expect(facts(structure)['Sector rank']).toBe('#2');
    expect(structure.notes).toEqual(['no regime data']);
  });

  it('names a single regime reading on its own', () => {
    const structure = axis(
      ScorecardFixtures.scorecard({
        structure: {...card.structure, volatilityRegime: null, sectorRank: null},
      }),
      'Structure'
    );

    expect(facts(structure)['Regime']).toBe('rates rising');
    expect(facts(structure)['Sector rank']).toBeUndefined();
  });

  it('reads a missing structure score as not scored, with the reasons as notes', () => {
    const structure = axis(
      ScorecardFixtures.scorecard({
        structure: {
          ...card.structure,
          rawScore: null,
          adjustedScore: null,
          adjustmentPoints: 0,
          regimeRationale: [],
          rsByWindow: {},
          returnByWindow: {},
          extensionFromMa50: null,
          todayZScore: null,
          volumeRatio: null,
          distanceFrom63dHigh: null,
          notEvaluableReasons: ['no_price_history'],
        },
      }),
      'Structure'
    );

    expect(structure.summary).toBe(SCORECARD_NOT_SCORED);
    expect(facts(structure)['Raw score']).toBeUndefined();
    expect(structure.notes).toEqual(['no price history']);
  });

  it('reads fundamentals as a score with the facts it read', () => {
    const fundamentals = axis(card, 'Fundamentals');

    expect(fundamentals.summary).toBe('80 / 100');
    expect(lines(fundamentals)).toEqual([
      'Revenue YoY: +62.0%',
      'Gross margin: 75.1%',
      'Gross margin trend: +1.2 pp',
      'EPS YoY: +80.0%',
      'Basis: quarterly yoy',
    ]);
    expect(fundamentals.notes).toEqual([]);
  });

  it('reads missing fundamentals as not scored and drops the empty facts', () => {
    const fundamentals = axis(
      ScorecardFixtures.scorecard({
        fundamentals: {
          score: null,
          revenueYoy: null,
          grossMarginLatest: null,
          grossMarginTrend: null,
          epsYoy: null,
          basis: '',
          notEvaluableReasons: ['no_fundamentals'],
        },
      }),
      'Fundamentals'
    );

    expect(fundamentals.summary).toBe(SCORECARD_NOT_SCORED);
    expect(fundamentals.facts).toEqual([]);
    expect(fundamentals.notes).toEqual(['no fundamentals']);
  });

  it('reads a null basis as no basis fact', () => {
    const fundamentals = axis(
      ScorecardFixtures.scorecard({fundamentals: {...card.fundamentals, basis: null}}),
      'Fundamentals'
    );

    expect(facts(fundamentals)['Basis']).toBeUndefined();
  });

  it('reads crowding as its class with the thresholds that classify it', () => {
    const crowding = axis(card, 'Crowding');

    expect(crowding.summary).toBe('Extended');
    expect(lines(crowding)).toEqual([
      'Extension from 50-day MA: +24.0%',
      'Volume vs average: 1.80x',
      'Early at or below: 5.0%',
      'Extended at or above: 20.0% with volume 1.50x',
    ]);
    expect(crowding.notes).toEqual([]);
  });

  it('reads an unclassified crowding axis as unknown, not Normal', () => {
    const crowding = axis(
      ScorecardFixtures.scorecard({
        crowding: {
          ...card.crowding,
          class: null,
          extensionFromMa50: null,
          volumeRatio: null,
          notEvaluableReason: 'no_extension',
        },
      }),
      'Crowding'
    );

    expect(crowding.summary).toBe(SCORECARD_UNKNOWN);
    expect(crowding.notes).toEqual(['no extension']);
  });

  it('reads valuation as the P/E premium to its own average with the raw facts', () => {
    const valuation = axis(card, 'Valuation');

    expect(valuation.summary).toBe('P/E +29.2% vs its own average');
    expect(lines(valuation)).toEqual([
      'Trailing P/E: 31.0',
      'Own 5-year average P/E: 24.0',
      'Forward P/E: 26.4',
      'Price: $120.00',
      'Consensus target: $138.00',
      'Implied upside: +15.0%',
    ]);
    expect(valuation.notes).toEqual([]);
  });

  it('reads an unknown premium as unknown, with the reason and the stale note first', () => {
    const valuation = axis(
      ScorecardFixtures.scorecard({
        valuation: {
          ...card.valuation,
          trailingPeFiveYearAvg: null,
          historyWindowYears: null,
          premiumPct: null,
          price: null,
          consensusTarget: null,
          impliedUpsidePct: null,
          isStale: true,
          notEvaluableReason: 'no_five_year_average',
        },
      }),
      'Valuation'
    );

    expect(valuation.summary).toBe(SCORECARD_UNKNOWN);
    expect(facts(valuation)).toEqual({'Trailing P/E': '31.0', 'Forward P/E': '26.4'});
    expect(valuation.notes).toEqual([SCORECARD_STALE_NOTE, 'no five year average']);
  });

  it('labels the average without a window when the history window is unknown', () => {
    const valuation = axis(
      ScorecardFixtures.scorecard({valuation: {...card.valuation, historyWindowYears: null}}),
      'Valuation'
    );

    expect(facts(valuation)['Own average P/E']).toBe('24.0');
  });

  it('reads a not-applicable valuation as outside the universe with no facts', () => {
    const valuation = axis(
      ScorecardFixtures.scorecard({valuation: {...card.valuation, notApplicable: true}}),
      'Valuation'
    );

    expect(valuation.summary).toBe(SCORECARD_NOT_APPLICABLE);
    expect(valuation.facts).toEqual([]);
    expect(valuation.notes).toEqual([]);
  });
});

describe('ScorecardUtils.rules', () => {
  const rules = ScorecardUtils.rules(card.verdict);

  it('keeps the whole table in order with the fired and matched flags', () => {
    expect(rules.map(r => r.id)).toEqual(card.verdict.rules.map(r => r.id));
    expect(rules.filter(r => r.fired).map(r => r.id)).toEqual(['extended_and_rich']);
    expect(rules.find(r => r.id === 'buy')?.matched).toBe(true);
  });

  it('writes a comparison with its subject, operator, threshold and the value it read', () => {
    expect(rules.find(r => r.id === 'weak_structure')?.conditions).toEqual([
      {text: 'Adjusted structure < 30', actual: '88', met: false},
    ]);
  });

  it('carries the valuation unit on the threshold and the value', () => {
    expect(rules.find(r => r.id === 'extended_and_rich')?.conditions).toEqual([
      {text: 'Crowding is Extended', actual: 'Extended', met: true},
      {text: 'P/E premium to its 5-year average > 0%', actual: '29.2%', met: true},
    ]);
  });

  it('writes a token condition in words', () => {
    expect(rules.find(r => r.id === 'outside_universe')?.conditions).toEqual([
      {text: 'Valuation is not applicable', actual: 'applicable', met: false},
    ]);
  });

  it('writes a missing-axis test without a threshold', () => {
    expect(rules.find(r => r.id === 'buy_fundamentals_missing')?.conditions).toEqual([
      {text: 'Fundamentals is missing', actual: '80', met: false},
    ]);
  });

  it('reads a missing value as missing', () => {
    const [view] = ScorecardUtils.rules({
      ...card.verdict,
      rules: [
        ScorecardFixtures.rule('strong_buy', 'StrongBuy', false, [
          ScorecardFixtures.condition('Fundamentals', 'AtLeast', '70', null, false),
          ScorecardFixtures.condition('Crowding', 'IsNot', 'Extended', null, false),
        ]),
      ],
    });

    expect(view.conditions.map(c => c.actual)).toEqual([SCORECARD_MISSING, SCORECARD_MISSING]);
    expect(view.conditions[1].text).toBe('Crowding is not Extended');
  });
});

describe('ScorecardUtils.firedRule', () => {
  it('returns the description of the rule that fired', () => {
    expect(ScorecardUtils.firedRule(card.verdict)).toBe('Rule extended_and_rich.');
  });

  it('returns null when the table names no such rule', () => {
    expect(ScorecardUtils.firedRule({...card.verdict, firedRuleId: 'gone'})).toBeNull();
  });
});

describe('ScorecardUtils.signedPercent', () => {
  it('signs a positive fraction', () => {
    expect(ScorecardUtils.signedPercent(0.123)).toBe('+12.3%');
  });

  it('signs a negative fraction', () => {
    expect(ScorecardUtils.signedPercent(-0.05)).toBe('-5.0%');
  });

  it('leaves zero unsigned', () => {
    expect(ScorecardUtils.signedPercent(0)).toBe('0.0%');
    expect(ScorecardUtils.signedPercent(-0.0001)).toBe('0.0%');
  });

  it('returns null for a missing value', () => {
    expect(ScorecardUtils.signedPercent(null)).toBeNull();
  });
});

describe('ScorecardUtils.humanize', () => {
  it('turns a snake_case code into words', () => {
    expect(ScorecardUtils.humanize('no_regime_data')).toBe('no regime data');
  });

  it('leaves a plain word alone', () => {
    expect(ScorecardUtils.humanize('Extended')).toBe('Extended');
  });
});
