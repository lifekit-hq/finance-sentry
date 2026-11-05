import {type Nullable} from '@lifekit-hq/ui';

import {
  UNIVERSE_ALL_FILTER,
  UNIVERSE_CROWDING_ORDER,
  UNIVERSE_MISSING,
  UNIVERSE_VERDICT_ORDER,
} from '../constants/universe-screen/universe-screen.constants';
import {
  type UniverseRow,
  type UniverseRowView,
  type UniverseSortKey,
  type UniverseVerdictFilter,
} from '../models/universe-screen/universe-screen.model';

const LAST = Number.POSITIVE_INFINITY;
/** Two decimals: scale up, round, scale back. */
const TWO_DECIMALS = 100;

/**
 * Pure helpers of the universe screen (spec 726 US1). Every sort key is one raw fact of the row or the verdict
 * word: nothing here blends axes into a rank, and a name with no value for the key sorts last.
 */
export class UniverseScreenUtils {
  /** The rows carrying the filtered verdict word, or every row for `All`. */
  public static filter(rows: readonly UniverseRow[], filter: UniverseVerdictFilter): UniverseRow[] {
    if (filter === UNIVERSE_ALL_FILTER) {
      return [...rows];
    }
    return rows.filter(r => r.verdict?.word === filter);
  }

  /** A stable ordering by one key; ties fall to the ticker. */
  public static sort(rows: readonly UniverseRow[], key: UniverseSortKey): UniverseRow[] {
    return [...rows].sort((a, b) => {
      const byKey = UniverseScreenUtils.compare(a, b, key);
      return byKey !== 0 ? byKey : a.symbol.localeCompare(b.symbol);
    });
  }

  public static toView(row: UniverseRow): UniverseRowView {
    const structure = row.structure;
    const adjusted = structure?.adjusted ?? structure?.raw ?? null;
    const showRaw =
      structure?.raw != null && structure.adjusted != null && structure.raw !== structure.adjusted;
    return {
      row,
      structureText: UniverseScreenUtils.number(adjusted),
      structureRawText: showRaw ? `raw ${structure?.raw}` : '',
      fundamentalsText: UniverseScreenUtils.number(row.fundamentals?.score),
      crowdingText: row.crowding?.class ?? UNIVERSE_MISSING,
      valuationText: UniverseScreenUtils.valuation(row),
      weightText:
        row.suggestedWeightPct == null
          ? ''
          : `${UniverseScreenUtils.percent(row.suggestedWeightPct)}%`,
      scoredAtText: row.scoredAt ?? '',
      verdictRuleText: row.verdict?.firedRule ?? '',
      notEvaluableTexts: [
        ...(structure?.notEvaluable ?? []),
        ...(row.fundamentals?.notEvaluable ?? []),
        ...(row.valuation?.notEvaluable ? [row.valuation.notEvaluable] : []),
      ].map(UniverseScreenUtils.humanise),
    };
  }

  /** `63.5` as `63.5`, `12` as `12`: at most two decimals, no trailing zeros. */
  public static percent(value: number): string {
    return `${Math.round(value * TWO_DECIMALS) / TWO_DECIMALS}`;
  }

  /** `no_five_year_average` as `no five year average`. */
  public static humanise(reason: string): string {
    return reason.replaceAll('_', ' ');
  }

  private static number(value: Nullable<number> | undefined): string {
    return value == null ? UNIVERSE_MISSING : `${value}`;
  }

  private static valuation(row: UniverseRow): string {
    const v = row.valuation;
    if (v == null) {
      return UNIVERSE_MISSING;
    }
    if (v.notApplicable) {
      return 'not applicable';
    }
    if (v.trailingPe == null || v.trailingPeFiveYearAvg == null) {
      return v.notEvaluable ? UniverseScreenUtils.humanise(v.notEvaluable) : UNIVERSE_MISSING;
    }
    const premium =
      v.premiumPct == null
        ? ''
        : ` (${v.premiumPct > 0 ? '+' : ''}${UniverseScreenUtils.percent(v.premiumPct)}%)`;
    return `P/E ${UniverseScreenUtils.percent(v.trailingPe)} vs 5y avg ${UniverseScreenUtils.percent(v.trailingPeFiveYearAvg)}${premium}`;
  }

  /** Negative: `a` first. Descending keys flip the sign of the fact; a missing fact is last either way. */
  private static compare(a: UniverseRow, b: UniverseRow, key: UniverseSortKey): number {
    const [x, y] = [UniverseScreenUtils.keyOf(a, key), UniverseScreenUtils.keyOf(b, key)];
    if (x === y) {
      return 0;
    }
    return x < y ? -1 : 1;
  }

  /** The row's value for the key, ascending, with `LAST` standing for a missing fact. */
  private static keyOf(row: UniverseRow, key: UniverseSortKey): number | string {
    switch (key) {
      case 'construction':
        return row.constructionRank ?? LAST;
      case 'structure':
        return row.structure?.raw == null ? LAST : -row.structure.raw;
      case 'fundamentals':
        return row.fundamentals?.score == null ? LAST : -row.fundamentals.score;
      case 'crowding':
        return row.crowding?.class == null
          ? LAST
          : (UNIVERSE_CROWDING_ORDER[row.crowding.class] ?? LAST);
      case 'valuation':
        return row.valuation?.premiumPct ?? LAST;
      case 'verdict':
        return row.verdict == null ? LAST : (UNIVERSE_VERDICT_ORDER[row.verdict.word] ?? LAST);
      case 'symbol':
        return row.symbol;
    }
  }
}
