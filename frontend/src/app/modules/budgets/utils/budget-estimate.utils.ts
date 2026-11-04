import {type Nullable} from '@lifekit-hq/ui';

import {APP_LOCALE} from '../../../shared/constants/date/date.constants';
import {COUNTERPARTY_ROLE_LINES} from '../constants/budget/budget.constants';
import {type SkippedEstimateMonth} from '../models/budget/budget.model';

const MONTH_KEY_PARTS = 2;

export class BudgetEstimateUtils {
  /** "2026-09" -> "September 2026". */
  public static monthLabel(month: string): string {
    const [year, mon] = month.split('-').map(Number);
    return new Date(year, mon - 1, 1).toLocaleDateString(APP_LOCALE, {
      month: 'long',
      year: 'numeric',
    });
  }

  /** ["a"] -> "a", ["a","b"] -> "a and b", ["a","b","c"] -> "a, b and c". */
  public static joinList(items: string[]): string {
    if (items.length <= 1) {
      return items.join('');
    }
    return `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
  }

  /** Whether a ledger category filter can find the line's spend (false for the counterparty-role lines). */
  public static hasTransactionCategory(line: string): boolean {
    return !COUNTERPARTY_ROLE_LINES.includes(line);
  }

  /** One plain-words sentence per reason for the months the estimates leave out, or null when none are. */
  public static skippedNote(skipped: SkippedEstimateMonth[]): Nullable<string> {
    const named = (reason: SkippedEstimateMonth['reason']) =>
      skipped
        .filter(s => s.reason === reason && s.month.split('-').length === MONTH_KEY_PARTS)
        .map(s => BudgetEstimateUtils.monthLabel(s.month));
    const sentences: string[] = [];

    const excluded = named('excluded');
    if (excluded.length > 0) {
      sentences.push(
        `${BudgetEstimateUtils.joinList(excluded)} ${excluded.length === 1 ? 'is' : 'are'} left out of these estimates.`
      );
    }
    const gaps = named('coverage-gap');
    if (gaps.length > 0) {
      sentences.push(
        `${BudgetEstimateUtils.joinList(gaps)} ${gaps.length === 1 ? 'is' : 'are'} left out because the bank link has a gap there, so a missing month is not read as a cheap one.`
      );
    }
    return sentences.length > 0 ? sentences.join(' ') : null;
  }

  /** Share of the estimate used, 0-100 (clamped); a zero estimate reads as full once anything is spent. */
  public static usedPct(spent: number, estimate: number): number {
    const max = 100;
    if (estimate <= 0) {
      return spent > 0 ? max : 0;
    }
    return Math.min((spent / estimate) * max, max);
  }
}
