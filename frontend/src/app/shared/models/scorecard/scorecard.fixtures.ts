import {type Nullable} from '@lifekit-hq/ui';

import {type ScorecardDto, type VerdictConditionDto, type VerdictRuleDto} from './scorecard.model';

/** Spec fixtures for the scorecard drill-down (spec 726 US2); never imported by app code. */
export class ScorecardFixtures {
  /** A chased, rich name: strong structure and fundamentals, Extended and 29.2% over its own P/E average. */
  public static scorecard(overrides: Partial<ScorecardDto> = {}): ScorecardDto {
    return {
      symbol: 'NVDA',
      generatedAt: '2026-10-15T09:00:00Z',
      formulaVersion: 2,
      structure: {
        rawScore: 92,
        adjustedScore: 88,
        adjustmentPoints: -4,
        regimeRationale: ['risk_off_haircut'],
        volatilityRegime: 'elevated',
        ratesRegime: 'rising',
        regimeAsOf: '2026-10-14T00:00:00Z',
        rsByWindow: {'63': 0.18, '21': 0.07},
        returnByWindow: {'21': 0.11, '63': null},
        extensionFromMa50: 0.24,
        todayZScore: 1.234,
        volumeRatio: 1.8,
        sectorRank: 2,
        sectorRankDelta: 3,
        distanceFrom63dHigh: -0.02,
        notEvaluableReasons: [],
      },
      fundamentals: {
        score: 80,
        revenueYoy: 0.62,
        grossMarginLatest: 0.751,
        grossMarginTrend: 0.012,
        epsYoy: 0.8,
        basis: 'quarterly_yoy',
        notEvaluableReasons: [],
      },
      crowding: {
        class: 'Extended',
        extensionFromMa50: 0.24,
        volumeRatio: 1.8,
        earlyExtensionThreshold: 0.05,
        extendedExtensionThreshold: 0.2,
        extendedVolumeRatioThreshold: 1.5,
        notEvaluableReason: null,
      },
      valuation: {
        notApplicable: false,
        trailingPe: 31,
        trailingPeFiveYearAvg: 24,
        historyWindowYears: 5,
        premiumPct: 29.2,
        forwardPe: 26.4,
        price: 120,
        consensusTarget: 138,
        impliedUpsidePct: 15,
        currency: 'USD',
        isStale: false,
        notEvaluableReason: null,
      },
      verdict: {
        word: 'Avoid',
        rulesVersion: 1,
        firedRuleId: 'extended_and_rich',
        structureBasis: 'adjusted',
        rules: [
          ScorecardFixtures.rule('outside_universe', 'NotScored', false, [
            ScorecardFixtures.condition('Valuation', 'Is', 'not_applicable', 'applicable', false),
          ]),
          ScorecardFixtures.rule('weak_structure', 'Avoid', false, [
            ScorecardFixtures.condition('Structure', 'LessThan', '30', '88', false),
          ]),
          ScorecardFixtures.rule(
            'extended_and_rich',
            'Avoid',
            true,
            [
              ScorecardFixtures.condition('Crowding', 'Is', 'Extended', 'Extended', true),
              ScorecardFixtures.condition('Valuation', 'GreaterThan', '0', '29.2', true),
            ],
            true
          ),
          ScorecardFixtures.rule('buy', 'Buy', true, [
            ScorecardFixtures.condition('Structure', 'AtLeast', '50', '88', true),
            ScorecardFixtures.condition('Fundamentals', 'AtLeast', '50', '80', true),
          ]),
          ScorecardFixtures.rule('buy_fundamentals_missing', 'Buy', false, [
            ScorecardFixtures.condition('Fundamentals', 'IsMissing', null, '80', false),
          ]),
          ScorecardFixtures.rule('otherwise', 'Wait', true, []),
        ],
        reasons: [
          {
            axis: 'Structure',
            status: 'Pass',
            text: 'Structure 88 clears the BUY bar of 50 and the STRONG BUY bar of 70.',
            decisive: false,
          },
          {
            axis: 'Fundamentals',
            status: 'Pass',
            text: 'Fundamentals 80 clear the BUY bar of 50 and the STRONG BUY bar of 70.',
            decisive: false,
          },
          {
            axis: 'Crowding',
            status: 'Fail',
            text: 'Crowding is Extended: STRONG BUY needs it not Extended, and Extended with a P/E above its own average is AVOID.',
            decisive: true,
          },
          {
            axis: 'Valuation',
            status: 'Fail',
            text: 'P/E premium to its 5-year average is +29.2%: STRONG BUY allows at most 10%.',
            decisive: true,
          },
        ],
        turnsBuyWhen:
          'crowding is no longer Extended or the P/E premium falls to 0% or below (reads +29.2%)',
      },
      ...overrides,
    };
  }

  public static rule(
    id: string,
    word: VerdictRuleDto['word'],
    matched: boolean,
    conditions: VerdictConditionDto[],
    fired = false
  ): VerdictRuleDto {
    return {id, word, description: `Rule ${id}.`, matched, fired, conditions};
  }

  public static condition(
    axis: VerdictConditionDto['axis'],
    operator: VerdictConditionDto['operator'],
    threshold: Nullable<string>,
    actual: Nullable<string>,
    met: boolean
  ): VerdictConditionDto {
    return {axis, operator, threshold, actual, met};
  }
}
