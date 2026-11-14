import {type UniverseRow, type UniverseScreen} from './universe-screen.model';

/** Spec fixtures for the universe screen (spec 726 US1); synthetic data, never imported by app code. */
export class UniverseScreenFixtures {
  /** A scored name: BUY, structure 68 adjusted from 64 raw, P/E 28 against a 24 average. */
  public static row(overrides: Partial<UniverseRow> = {}): UniverseRow {
    return {
      symbol: 'AAA',
      sources: ['coverage'],
      sector: 'Technology',
      industry: 'Software - Infrastructure',
      failureReason: null,
      scoredAt: '2026-11-05T09:00:00Z',
      verdict: {
        word: 'Buy',
        rulesVersion: 1,
        firedRuleId: 'buy',
        firedRule: 'Adjusted structure at least 50 and fundamentals at least 50.',
      },
      structure: {raw: 64, adjusted: 68, basis: 'adjusted', notEvaluable: []},
      fundamentals: {score: 72, notEvaluable: []},
      crowding: {class: 'Normal', extensionFromMa50: 0.04},
      valuation: {
        notApplicable: false,
        trailingPe: 28,
        trailingPeFiveYearAvg: 24,
        premiumPct: 16.7,
        impliedUpsidePct: 9.1,
        notEvaluable: null,
        stale: false,
      },
      constructionRank: 1,
      suggestedWeightPct: 12,
      reason:
        'Passed the Growth gates with BUY; ranked #1 by structure then fundamentals; 12% (equal weight, capped at 12% (archetype default)).',
      ...overrides,
    };
  }

  /** A name whose fundamentals cannot be evaluated: the axis reads "no data", never a neutral value. */
  public static rowWithoutFundamentals(symbol = 'BBB'): UniverseRow {
    return UniverseScreenFixtures.row({
      symbol,
      verdict: {
        word: 'Wait',
        rulesVersion: 1,
        firedRuleId: 'otherwise',
        firedRule: 'No earlier rule matched.',
      },
      fundamentals: {score: null, notEvaluable: ['too_few_periods']},
      constructionRank: null,
      suggestedWeightPct: null,
      reason: 'The verdict is WAIT, which Growth does not build from (STRONG BUY, BUY).',
    });
  }

  public static failedRow(symbol = 'CCC'): UniverseRow {
    return {
      symbol,
      sources: ['watchlist'],
      sector: null,
      industry: null,
      failureReason: 'the scorecard could not be read',
      scoredAt: null,
      verdict: null,
      structure: null,
      fundamentals: null,
      crowding: null,
      valuation: null,
      constructionRank: null,
      suggestedWeightPct: null,
      reason: 'Not scored: the scorecard could not be read.',
    };
  }

  public static screen(overrides: Partial<UniverseScreen> = {}): UniverseScreen {
    return {
      archetype: 'growth',
      archetypes: [
        {
          key: 'balanced',
          label: 'Balanced',
          description: 'A wider book of sound-fundamentals names.',
          eligibleVerdicts: ['StrongBuy', 'Buy'],
          minStructure: 50,
          minFundamentals: 60,
          excludeExtended: true,
          ordering: ['fundamentals', 'structure'],
        },
        {
          key: 'growth',
          label: 'Growth',
          description: 'A concentrated book of the strongest-structure names.',
          eligibleVerdicts: ['StrongBuy', 'Buy'],
          minStructure: 60,
          minFundamentals: null,
          excludeExtended: true,
          ordering: ['structure', 'fundamentals'],
        },
      ],
      generatedAt: '2026-11-05T09:00:00Z',
      universeCap: 40,
      truncated: false,
      construction: {
        constraints: [
          {
            key: 'equitySleevePct',
            label: 'Equities sleeve',
            value: 90,
            source: 'archetype default',
          },
          {
            key: 'maxPositionPct',
            label: 'Largest single position',
            value: 8,
            source: 'your risk rules',
          },
          {key: 'cashFloorPct', label: 'Cash floor', value: 5, source: 'archetype default'},
          {key: 'nameCount', label: 'Most names held', value: 10, source: 'archetype default'},
        ],
        sleeves: [
          {assetClass: 'Equities', weightPct: 12, builtFromUniverse: true, note: null},
          {assetClass: 'Cash', weightPct: 88, builtFromUniverse: false, note: null},
        ],
        cashPct: 88,
        notes: ['Only 1 of the 10 names Growth would hold pass its gates.'],
        refusal: null,
        namesHeld: 1,
      },
      rows: [
        UniverseScreenFixtures.row(),
        UniverseScreenFixtures.rowWithoutFundamentals(),
        UniverseScreenFixtures.failedRow(),
      ],
      ...overrides,
    };
  }
}
