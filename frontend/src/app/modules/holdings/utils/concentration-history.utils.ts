import {type Nullable} from '@lifekit-hq/ui';

import {
  CONCENTRATION_DOMAIN_PADDING,
  CONCENTRATION_PERCENT_DIGITS,
  CONCENTRATION_PERCENT_POINTS,
} from '../constants/risk-picture/concentration-history.constants';
import {
  type ConcentrationHistory,
  type ConcentrationMeasure,
  type ConcentrationPoint,
  type ConcentrationView,
} from '../models/risk-picture/risk-picture.model';

const LABELS: Record<ConcentrationMeasure, string> = {
  top3: 'Top three share',
  crypto: 'Crypto share',
  cash: 'Cash share',
};

export class ConcentrationHistoryUtils {
  /** The chart series and summary line for one measure; null when the history is too short or the measure has no day. */
  public static view(
    history: ConcentrationHistory,
    measure: ConcentrationMeasure
  ): Nullable<ConcentrationView> {
    if (history.reason) {
      return null;
    }
    const chart = history.points.flatMap(point => {
      const value = ConcentrationHistoryUtils.valueOf(point, measure);
      return value === null
        ? []
        : [
            {
              label: point.date,
              value: value * CONCENTRATION_PERCENT_POINTS,
              time: Date.parse(point.date),
            },
          ];
    });
    if (chart.length === 0) {
      return null;
    }
    const latest = chart[chart.length - 1].value;
    const target = ConcentrationHistoryUtils.targetOf(history, measure);
    const targetPoints =
      target === null
        ? []
        : chart.map(point => ({...point, value: target * CONCENTRATION_PERCENT_POINTS}));
    const values = [...chart, ...targetPoints].map(point => point.value);
    return {
      measure,
      label: LABELS[measure],
      chart,
      target: targetPoints,
      domain: {
        min: Math.min(...values) - CONCENTRATION_DOMAIN_PADDING,
        max: Math.max(...values) + CONCENTRATION_DOMAIN_PADDING,
      },
      summary: ConcentrationHistoryUtils.summary(latest, target),
    };
  }

  private static summary(latest: number, target: Nullable<number>): string {
    const now = `Now ${ConcentrationHistoryUtils.percent(latest)}`;
    if (target === null) {
      return now;
    }
    const drift = latest - target * CONCENTRATION_PERCENT_POINTS;
    const sign = drift > 0 ? '+' : '';
    return `${now}, target ${ConcentrationHistoryUtils.percent(target * CONCENTRATION_PERCENT_POINTS)}, drift ${sign}${drift.toFixed(CONCENTRATION_PERCENT_DIGITS)} pts`;
  }

  private static percent(points: number): string {
    return `${points.toFixed(CONCENTRATION_PERCENT_DIGITS)}%`;
  }

  private static valueOf(
    point: ConcentrationPoint,
    measure: ConcentrationMeasure
  ): Nullable<number> {
    switch (measure) {
      case 'top3':
        return point.top3Share;
      case 'crypto':
        return point.cryptoShare;
      case 'cash':
        return point.cashShare;
    }
  }

  private static targetOf(
    history: ConcentrationHistory,
    measure: ConcentrationMeasure
  ): Nullable<number> {
    switch (measure) {
      case 'crypto':
        return history.cryptoTarget;
      case 'cash':
        return history.cashTarget;
      case 'top3':
        return null;
    }
  }
}
