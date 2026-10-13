import {
  RULES_PER_AXIS,
  WHEEL_LABEL_OFFSET,
  WHEEL_RADIUS,
  WHEEL_SIZE,
} from '../constants/dossier/dossier-checks.constants';
import {
  type DossierCheckAxisDto,
  type DossierCheckRuleDto,
  type DossierChecksDto,
} from '../models/dossier/dossier.model';
import {
  type CheckAxisView,
  type CheckRuleView,
  type RuleState,
  type WheelAxisView,
  type WheelView,
} from '../models/dossier/dossier-view.model';

const HALF = 2;
const FULL_TURN = HALF * Math.PI;
const QUARTER_TURN = Math.PI / HALF;
/** A polygon needs three spokes; with fewer the wheel says nothing a list does not. */
const MIN_WHEEL_AXES = 3;
/** Labels within this cosine of vertical centre on their spoke. */
const LABEL_CENTRE_BAND = 0.3;
const COORD_DIGITS = 1;
const VALUE_DIGITS = 1;
const INNER_RING = 0.5;
const RING_FRACTIONS = [INNER_RING, 1];

export class DossierChecksUtils {
  /** Every axis with its rules spelled out as a fact against its bar. */
  public static axes(checks: Nullable<DossierChecksDto>): CheckAxisView[] {
    return (checks?.axes ?? []).map(axis => DossierChecksUtils.axis(axis));
  }

  public static axis(axis: DossierCheckAxisDto): CheckAxisView {
    return {
      key: axis.key,
      label: axis.label,
      passed: axis.passed,
      evaluated: axis.evaluated,
      total: axis.rules.length,
      rules: axis.rules.map(rule => DossierChecksUtils.rule(rule)),
    };
  }

  public static rule(rule: DossierCheckRuleDto): CheckRuleView {
    return {
      key: rule.key,
      label: rule.label,
      state: DossierChecksUtils.state(rule.passed),
      detail: DossierChecksUtils.detail(rule),
      note: rule.note,
    };
  }

  public static state(passed: Nullable<boolean>): RuleState {
    if (passed === null) {
      return 'unknown';
    }
    return passed ? 'pass' : 'fail';
  }

  /** "28.5x vs 25.3x", "12.2%" (no bar known); null when the rule read no number. */
  public static detail(rule: DossierCheckRuleDto): Nullable<string> {
    if (rule.value === null || rule.unit === 'none') {
      return null;
    }
    const value = DossierChecksUtils.formatValue(rule.value, rule.unit);
    return rule.threshold === null
      ? value
      : `${value} vs ${DossierChecksUtils.formatValue(rule.threshold, rule.unit)}`;
  }

  public static formatValue(value: number, unit: DossierCheckRuleDto['unit']): string {
    const text = Number(value.toFixed(VALUE_DIGITS)).toString();
    if (unit === 'percent') {
      return `${text}%`;
    }
    return unit === 'multiple' ? `${text}x` : text;
  }

  /**
   * The summary wheel: one spoke per axis, the shape reaching out by the share of its rules that
   * passed. Null below three axes (crypto keeps only momentum and fit), where the list stands alone.
   */
  public static wheel(checks: Nullable<DossierChecksDto>): Nullable<WheelView> {
    const axes = checks?.axes ?? [];
    if (axes.length < MIN_WHEEL_AXES) {
      return null;
    }
    const centre = WHEEL_SIZE / HALF;
    const angleOf = (index: number) => (index / axes.length) * FULL_TURN - QUARTER_TURN;
    const pointAt = (index: number, radius: number) => {
      const angle = angleOf(index);
      return {
        x: DossierChecksUtils.round(centre + radius * Math.cos(angle)),
        y: DossierChecksUtils.round(centre + radius * Math.sin(angle)),
      };
    };
    const polygon = (radiusOf: (index: number) => number) =>
      axes
        .map((_axis, i) => {
          const {x, y} = pointAt(i, radiusOf(i));
          return `${x},${y}`;
        })
        .join(' ');

    const wheelAxes: WheelAxisView[] = axes.map((axis, i) => {
      const end = pointAt(i, WHEEL_RADIUS);
      const label = pointAt(i, WHEEL_RADIUS + WHEEL_LABEL_OFFSET);
      const cos = Math.cos(angleOf(i));
      const anchor =
        cos > LABEL_CENTRE_BAND ? 'start' : cos < -LABEL_CENTRE_BAND ? 'end' : 'middle';
      return {
        key: axis.key,
        label: axis.label,
        x: end.x,
        y: end.y,
        labelX: label.x,
        labelY: label.y,
        anchor,
      };
    });

    return {
      size: WHEEL_SIZE,
      centre,
      axes: wheelAxes,
      rings: RING_FRACTIONS.map(fraction => polygon(() => WHEEL_RADIUS * fraction)),
      shape: polygon(
        i => (WHEEL_RADIUS * Math.min(axes[i].passed, RULES_PER_AXIS)) / RULES_PER_AXIS
      ),
    };
  }

  private static round(value: number): number {
    return Number(value.toFixed(COORD_DIGITS));
  }
}
