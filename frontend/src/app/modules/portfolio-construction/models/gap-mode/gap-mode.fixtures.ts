import {type GapMode, type NameGap} from './gap-mode.model';

/** Spec fixtures for gap mode (spec 726 US4); synthetic data, never imported by app code. */
export class GapModeFixtures {
  public static nameGap(overrides: Partial<NameGap> = {}): NameGap {
    return {
      symbol: 'AAA',
      sector: 'Technology',
      verdict: 'Buy',
      status: 'Underweight',
      heldOutsideTarget: false,
      currentPct: 9,
      targetPct: 12,
      gapPct: 3,
      currentUsd: 900,
      targetUsd: 1200,
      gapUsd: 300,
      reason: 'Target 12%. Held at 9%. Passed the Growth gates with BUY.',
      ...overrides,
    };
  }

  /** A book of 10 000 USD: AAA short, BBB missing, CCC over, ZZZ held outside the target. */
  public static gap(overrides: Partial<GapMode> = {}): GapMode {
    return {
      archetype: 'growth',
      generatedAt: '2026-11-14T08:00:00Z',
      label:
        'Informational only: what you hold against a rules-built target. It is not a recommendation to buy or sell, and nothing here places or prepares a trade.',
      hasBook: true,
      bookValueUsd: 10000,
      bookStale: false,
      staleSources: [],
      refusal: null,
      summary: {
        headline: 'Missing: BBB. Underweight: AAA. Overweight: CCC, ZZZ.',
        missingNames: ['BBB'],
        underweightNames: ['AAA'],
        overweightNames: ['CCC', 'ZZZ'],
        onTargetCount: 0,
      },
      sleeves: [
        {
          assetClass: 'Equities',
          status: 'Overweight',
          currentPct: 50,
          targetPct: 36,
          gapPct: -14,
          currentUsd: 5000,
          targetUsd: 3600,
          gapUsd: -1400,
        },
        {
          assetClass: 'Cash',
          status: 'Underweight',
          currentPct: 50,
          targetPct: 64,
          gapPct: 14,
          currentUsd: 5000,
          targetUsd: 6400,
          gapUsd: 1400,
        },
      ],
      sectors: [
        {
          sector: 'Technology',
          status: 'Overweight',
          currentPct: 34,
          targetPct: 24,
          gapPct: -10,
          currentUsd: 3400,
          targetUsd: 2400,
          gapUsd: -1000,
          symbols: ['AAA', 'CCC'],
        },
      ],
      names: [
        GapModeFixtures.nameGap({
          symbol: 'CCC',
          status: 'Overweight',
          currentPct: 25,
          gapPct: -13,
          currentUsd: 2500,
          gapUsd: -1300,
        }),
        GapModeFixtures.nameGap({
          symbol: 'BBB',
          sector: 'Energy',
          status: 'Missing',
          currentPct: 0,
          gapPct: 12,
          currentUsd: 0,
          gapUsd: 1200,
          reason: 'Target 12%. Not held. Passed the Growth gates with BUY.',
        }),
        GapModeFixtures.nameGap(),
        GapModeFixtures.nameGap({
          symbol: 'ZZZ',
          sector: null,
          verdict: 'Avoid',
          status: 'Overweight',
          heldOutsideTarget: true,
          currentPct: 5,
          targetPct: 0,
          gapPct: -5,
          currentUsd: 500,
          targetUsd: 0,
          gapUsd: -500,
          reason: 'Held outside the target: 5% of the book. The verdict is AVOID.',
        }),
      ],
      notes: [],
      ...overrides,
    };
  }
}
