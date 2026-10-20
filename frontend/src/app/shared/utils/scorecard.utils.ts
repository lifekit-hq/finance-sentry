import {type Nullable} from '@lifekit-hq/ui';

import {
  SCORECARD_MISSING,
  SCORECARD_NOT_APPLICABLE,
  SCORECARD_NOT_SCORED,
  SCORECARD_SCORE_OUT_OF,
  SCORECARD_STALE_NOTE,
  SCORECARD_UNKNOWN,
  VERDICT_AXIS_LABEL,
  VERDICT_CONDITION_SUBJECT,
  VERDICT_CONDITION_UNIT,
  VERDICT_OPERATOR_LABEL,
} from '../constants/scorecard/scorecard.constants';
import {
  type ScorecardAxisView,
  type ScorecardCrowdingAxis,
  type ScorecardDto,
  type ScorecardFact,
  type ScorecardFundamentalsAxis,
  type ScorecardStructureAxis,
  type ScorecardValuationAxis,
  type VerdictConditionDto,
  type VerdictConditionView,
  type VerdictDto,
  type VerdictRuleView,
} from '../models/scorecard/scorecard.model';
import {MoneyUtils} from './money.utils';

const PERCENT = 100;
const PERCENT_DIGITS = 1;
const RATIO_DIGITS = 2;
const PE_DIGITS = 1;
const UNARY_OPERATORS = new Set(['Is', 'IsNot', 'IsMissing']);

type MaybeFact = Nullable<ScorecardFact>;

/**
 * Turns a scorecard (spec 726 US2) into what its drill-down shows: each axis with the facts behind it,
 * and the rule table with each condition's threshold and the value it read. Display only: the word and
 * every condition's outcome come from the backend's rule table, never recomputed here.
 */
export class ScorecardUtils {
  /** The four axes in their fixed order: structure, fundamentals, crowding, valuation. */
  public static axes(card: ScorecardDto): ScorecardAxisView[] {
    return [
      ScorecardUtils.structure(card.structure),
      ScorecardUtils.fundamentals(card.fundamentals),
      ScorecardUtils.crowding(card.crowding),
      ScorecardUtils.valuation(card.valuation),
    ];
  }

  /** The whole ordered rule table, each condition in words with the value it read. */
  public static rules(verdict: VerdictDto): VerdictRuleView[] {
    return verdict.rules.map(rule => ({
      id: rule.id,
      word: rule.word,
      description: rule.description,
      matched: rule.matched,
      fired: rule.fired,
      conditions: rule.conditions.map(ScorecardUtils.condition),
    }));
  }

  /** The description of the rule that fired, or null when the table names none. */
  public static firedRule(verdict: VerdictDto): Nullable<string> {
    return verdict.rules.find(r => r.id === verdict.firedRuleId)?.description ?? null;
  }

  /** A fraction as a signed percent: 0.123 → "+12.3%", -0.05 → "-5.0%". */
  public static signedPercent(fraction: Nullable<number>): Nullable<string> {
    return fraction === null ? null : ScorecardUtils.signed(fraction * PERCENT, '%');
  }

  /** A snake_case reason code as words: "no_regime_data" → "no regime data". */
  public static humanize(code: string): string {
    return code.replaceAll('_', ' ');
  }

  private static condition(condition: VerdictConditionDto): VerdictConditionView {
    const unit = VERDICT_CONDITION_UNIT[condition.axis];
    const operator = VERDICT_OPERATOR_LABEL[condition.operator];
    const unary = UNARY_OPERATORS.has(condition.operator);
    const subject = unary
      ? VERDICT_AXIS_LABEL[condition.axis]
      : VERDICT_CONDITION_SUBJECT[condition.axis];
    const threshold =
      condition.threshold === null
        ? ''
        : ` ${unary ? ScorecardUtils.humanize(condition.threshold) : condition.threshold + unit}`;
    let actual = SCORECARD_MISSING;
    if (condition.actual !== null) {
      actual = unary ? ScorecardUtils.humanize(condition.actual) : condition.actual + unit;
    }
    return {text: `${subject} ${operator}${threshold}`, actual, met: condition.met};
  }

  private static structure(axis: ScorecardStructureAxis): ScorecardAxisView {
    let summary = SCORECARD_NOT_SCORED;
    if (axis.adjustedScore !== null) {
      summary = `${axis.adjustedScore} / ${SCORECARD_SCORE_OUT_OF}`;
      if (axis.adjustmentPoints !== 0) {
        summary += ` (raw ${axis.rawScore})`;
      }
    }
    const regime = [
      axis.volatilityRegime && `volatility ${axis.volatilityRegime}`,
      axis.ratesRegime && `rates ${axis.ratesRegime}`,
    ].filter(Boolean);
    const sectorRank =
      axis.sectorRank === null
        ? null
        : `#${axis.sectorRank}${axis.sectorRankDelta ? ` (${ScorecardUtils.signed(axis.sectorRankDelta, '', 0)})` : ''}`;
    return {
      axis: 'Structure',
      label: VERDICT_AXIS_LABEL.Structure,
      summary,
      facts: ScorecardUtils.present([
        ScorecardUtils.fact('Raw score', axis.rawScore?.toString() ?? null),
        ScorecardUtils.fact(
          'Regime adjustment',
          axis.adjustmentPoints === 0
            ? null
            : ScorecardUtils.signed(axis.adjustmentPoints, ' pts', 0)
        ),
        ScorecardUtils.fact('Regime', regime.length > 0 ? regime.join(', ') : null),
        ...ScorecardUtils.windows(axis.returnByWindow, 'Return'),
        ...ScorecardUtils.windows(axis.rsByWindow, 'Relative strength'),
        ScorecardUtils.fact(
          'Extension from 50-day MA',
          ScorecardUtils.signedPercent(axis.extensionFromMa50)
        ),
        ScorecardUtils.fact('Volume vs average', ScorecardUtils.ratio(axis.volumeRatio)),
        ScorecardUtils.fact('Today z-score', ScorecardUtils.fixed(axis.todayZScore, RATIO_DIGITS)),
        ScorecardUtils.fact('Sector rank', sectorRank),
        ScorecardUtils.fact(
          'From 63-day high',
          ScorecardUtils.signedPercent(axis.distanceFrom63dHigh)
        ),
      ]),
      notes: [...axis.regimeRationale, ...axis.notEvaluableReasons].map(ScorecardUtils.humanize),
    };
  }

  private static fundamentals(axis: ScorecardFundamentalsAxis): ScorecardAxisView {
    return {
      axis: 'Fundamentals',
      label: VERDICT_AXIS_LABEL.Fundamentals,
      summary:
        axis.score === null ? SCORECARD_NOT_SCORED : `${axis.score} / ${SCORECARD_SCORE_OUT_OF}`,
      facts: ScorecardUtils.present([
        ScorecardUtils.fact('Revenue YoY', ScorecardUtils.signedPercent(axis.revenueYoy)),
        ScorecardUtils.fact(
          'Gross margin',
          axis.grossMarginLatest === null
            ? null
            : `${(axis.grossMarginLatest * PERCENT).toFixed(PERCENT_DIGITS)}%`
        ),
        ScorecardUtils.fact(
          'Gross margin trend',
          axis.grossMarginTrend === null
            ? null
            : ScorecardUtils.signed(axis.grossMarginTrend * PERCENT, ' pp')
        ),
        ScorecardUtils.fact('EPS YoY', ScorecardUtils.signedPercent(axis.epsYoy)),
        ScorecardUtils.fact('Basis', axis.basis && ScorecardUtils.humanize(axis.basis)),
      ]),
      notes: axis.notEvaluableReasons.map(ScorecardUtils.humanize),
    };
  }

  private static crowding(axis: ScorecardCrowdingAxis): ScorecardAxisView {
    const early = `${(axis.earlyExtensionThreshold * PERCENT).toFixed(PERCENT_DIGITS)}%`;
    const extended =
      `${(axis.extendedExtensionThreshold * PERCENT).toFixed(PERCENT_DIGITS)}% with volume ` +
      `${axis.extendedVolumeRatioThreshold.toFixed(RATIO_DIGITS)}x`;
    return {
      axis: 'Crowding',
      label: VERDICT_AXIS_LABEL.Crowding,
      summary: axis.class ?? SCORECARD_UNKNOWN,
      facts: ScorecardUtils.present([
        ScorecardUtils.fact(
          'Extension from 50-day MA',
          ScorecardUtils.signedPercent(axis.extensionFromMa50)
        ),
        ScorecardUtils.fact('Volume vs average', ScorecardUtils.ratio(axis.volumeRatio)),
        ScorecardUtils.fact('Early at or below', early),
        ScorecardUtils.fact('Extended at or above', extended),
      ]),
      notes: axis.notEvaluableReason ? [ScorecardUtils.humanize(axis.notEvaluableReason)] : [],
    };
  }

  private static valuation(axis: ScorecardValuationAxis): ScorecardAxisView {
    if (axis.notApplicable) {
      return {
        axis: 'Valuation',
        label: VERDICT_AXIS_LABEL.Valuation,
        summary: SCORECARD_NOT_APPLICABLE,
        facts: [],
        notes: [],
      };
    }
    const average =
      axis.historyWindowYears === null
        ? 'Own average P/E'
        : `Own ${axis.historyWindowYears}-year average P/E`;
    const notes = axis.notEvaluableReason ? [ScorecardUtils.humanize(axis.notEvaluableReason)] : [];
    return {
      axis: 'Valuation',
      label: VERDICT_AXIS_LABEL.Valuation,
      summary:
        axis.premiumPct === null
          ? SCORECARD_UNKNOWN
          : `P/E ${ScorecardUtils.signed(axis.premiumPct, '%')} vs its own average`,
      facts: ScorecardUtils.present([
        ScorecardUtils.fact('Trailing P/E', ScorecardUtils.fixed(axis.trailingPe, PE_DIGITS)),
        ScorecardUtils.fact(average, ScorecardUtils.fixed(axis.trailingPeFiveYearAvg, PE_DIGITS)),
        ScorecardUtils.fact('Forward P/E', ScorecardUtils.fixed(axis.forwardPe, PE_DIGITS)),
        ScorecardUtils.fact(
          'Price',
          axis.price === null ? null : MoneyUtils.format(axis.price, axis.currency)
        ),
        ScorecardUtils.fact(
          'Consensus target',
          axis.consensusTarget === null
            ? null
            : MoneyUtils.format(axis.consensusTarget, axis.currency)
        ),
        ScorecardUtils.fact(
          'Implied upside',
          axis.impliedUpsidePct === null ? null : ScorecardUtils.signed(axis.impliedUpsidePct, '%')
        ),
      ]),
      notes: axis.isStale ? [SCORECARD_STALE_NOTE, ...notes] : notes,
    };
  }

  private static windows(byWindow: Record<string, Nullable<number>>, label: string): MaybeFact[] {
    return Object.keys(byWindow)
      .sort((a, b) => Number(a) - Number(b))
      .map(days =>
        ScorecardUtils.fact(`${label} ${days}d`, ScorecardUtils.signedPercent(byWindow[days]))
      );
  }

  private static fact(label: string, value: Nullable<string>): MaybeFact {
    return value === null || value === '' ? null : {label, value};
  }

  private static present(facts: MaybeFact[]): ScorecardFact[] {
    return facts.filter((f): f is ScorecardFact => f !== null);
  }

  private static signed(value: number, unit: string, digits = PERCENT_DIGITS): string {
    const text = Math.abs(value).toFixed(digits);
    if (Number(text) === 0) {
      return `${text}${unit}`;
    }
    return `${value > 0 ? '+' : '-'}${text}${unit}`;
  }

  private static ratio(value: Nullable<number>): Nullable<string> {
    return value === null ? null : `${value.toFixed(RATIO_DIGITS)}x`;
  }

  private static fixed(value: Nullable<number>, digits: number): Nullable<string> {
    return value === null ? null : value.toFixed(digits);
  }
}
