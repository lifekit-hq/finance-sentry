import {MoneyUtils} from '../../../shared/utils/money.utils';
import {type ThesisInvalidationTrigger} from '../models/dossier/dossier.model';

const PERCENT_FACTOR = 100;
const PERCENT_FRACTION_DIGITS = 1;

/* eslint-disable @typescript-eslint/naming-convention -- keys are the backend's snake_case metric codes */
const METRIC_LABELS: Readonly<Record<string, string>> = {
  gross_margin: 'Gross margin',
  operating_margin: 'Operating margin',
  net_margin: 'Net margin',
  revenue_yoy: 'Revenue growth (YoY)',
  net_income_yoy: 'Net income growth (YoY)',
  operating_income_yoy: 'Operating income growth (YoY)',
  eps_yoy: 'EPS growth (YoY)',
  revenue: 'Revenue',
  net_income: 'Net income',
  diluted_eps: 'Diluted EPS',
  price_drawdown: 'Price drawdown',
  price_return: 'Price return',
  relative_return: 'Return vs benchmark',
};
/* eslint-enable @typescript-eslint/naming-convention */

// Absolute amounts are dollars; every other metric is a fraction (0.35 = 35%).
const MONEY_METRICS: ReadonlySet<string> = new Set(['revenue', 'net_income', 'diluted_eps']);

const DIRECTION_PHRASES: Readonly<Record<string, string>> = {
  lessThan: 'falls below',
  greaterThan: 'rises above',
};

export class ThesisTriggerUtils {
  /** "Gross margin falls below 35.0% (via SOXX)" - the stored metric/direction codes as a sentence. */
  public static describe(trigger: ThesisInvalidationTrigger): string {
    const metric = METRIC_LABELS[trigger.metric] ?? ThesisTriggerUtils.humanize(trigger.metric);
    const direction = DIRECTION_PHRASES[trigger.direction] ?? trigger.direction;
    const sentence = `${metric} ${direction} ${ThesisTriggerUtils.formatThreshold(trigger)}`;
    return trigger.proxyTicker ? `${sentence} (via ${trigger.proxyTicker})` : sentence;
  }

  // A metric outside the known vocabulary still reads as words, never as a snake_case code.
  private static humanize(code: string): string {
    const words = code.replaceAll('_', ' ');
    return words.charAt(0).toUpperCase() + words.slice(1);
  }

  private static formatThreshold(trigger: ThesisInvalidationTrigger): string {
    if (MONEY_METRICS.has(trigger.metric)) {
      return MoneyUtils.format(trigger.threshold);
    }
    if (trigger.metric in METRIC_LABELS) {
      return `${(trigger.threshold * PERCENT_FACTOR).toFixed(PERCENT_FRACTION_DIGITS)}%`;
    }
    return String(trigger.threshold);
  }
}
