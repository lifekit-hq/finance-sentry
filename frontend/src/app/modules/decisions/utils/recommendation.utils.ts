import {type Nullable} from '@lifekit-hq/ui';

import {type RecommendationDto} from '../../../shared/models/recommendation/recommendation.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';

const EN_US = 'en-US';
const SHARE_FRACTION_DIGITS = 4;
const PCT_FRACTION_DIGITS = 1;
const WHOLE_DOLLARS = 0;
const ORDER_SEPARATOR = ' / ';

export type OutcomeTone = 'gain' | 'loss';

export class RecommendationUtils {
  /**
   * The size line under the headline: "Sell 348 shares (about $2,080), taking it from 23.0% to 15.0% of book".
   * Hold carries no size; it reads as the position kept at its current share.
   */
  public static sizeLine(rec: RecommendationDto): string {
    const share = RecommendationUtils.shareClause(rec);
    if (rec.action === 'Hold') {
      const current = RecommendationUtils.pct(rec.shareOfBookBeforePct);
      return current ? `Keep the position at ${current} of book` : 'Keep the position as it is';
    }

    const verb = rec.action === 'Add' ? 'Buy' : 'Sell';
    const shares = RecommendationUtils.shares(rec.sizeShares);
    const usd = rec.sizeUsd === null ? null : RecommendationUtils.aboutUsd(rec.sizeUsd);
    const all = rec.action === 'Exit' ? 'all ' : '';
    let line: string;
    if (shares && usd) {
      line = `${verb} ${all}${shares} shares (about ${usd})`;
    } else if (shares) {
      line = `${verb} ${all}${shares} shares`;
    } else if (usd) {
      line = `${verb} about ${usd}`;
    } else {
      line = rec.action === 'Exit' ? 'Sell the whole position' : `${verb} - size not set`;
    }
    return share ? `${line}, ${share}` : line;
  }

  /** "taking it from 23.0% to 15.0% of book", or empty when either share is unknown. */
  public static shareClause(rec: RecommendationDto): string {
    const before = RecommendationUtils.pct(rec.shareOfBookBeforePct);
    const after = RecommendationUtils.pct(rec.shareOfBookAfterPct);
    return before && after ? `taking it from ${before} to ${after} of book` : '';
  }

  /** What became of a decided proposal: placed or not at the broker, or why it closed undecided. */
  public static outcome(rec: RecommendationDto): string {
    switch (rec.status) {
      case 'Expired':
        return 'Expired without a decision';
      case 'Superseded':
        return 'Replaced by a newer proposal';
      case 'Rejected':
        return rec.decisionNote ? `Rejected: ${rec.decisionNote}` : 'Rejected';
      case 'Pending':
        return '';
      case 'Confirmed':
        break;
    }
    switch (rec.execution) {
      case 'Executed':
        return 'Placed at the broker';
      case 'Unexecuted':
        return 'Not placed within a week';
      case 'Pending':
        return 'Waiting for a matching fill';
      case 'NotApplicable':
        return 'No order needed';
    }
  }

  /**
   * The name against the S&P 500 since the decision, signed; empty until scored. Where the owner's decision has a
   * direction it reads as the decision's value ("decision +3.2 pts vs S&P 500"); a rejected Hold has none, so it
   * reads as the plain name figure ("name +3.2 pts vs S&P 500").
   */
  public static excessLine(rec: RecommendationDto): string {
    const value = rec.decisionValuePct;
    if (value !== null) {
      return `decision ${RecommendationUtils.signed(value)} pts vs S&P 500`;
    }
    return rec.excessReturnPct !== null && rec.status === 'Rejected'
      ? `name ${RecommendationUtils.signed(rec.excessReturnPct)} pts vs S&P 500`
      : '';
  }

  /** Gain when the decision added value against the S&P 500 since it was made, loss when it cost value; none at par or without one. */
  public static excessTone(rec: RecommendationDto): Nullable<OutcomeTone> {
    const value = rec.decisionValuePct;
    if (value === null || value === 0) {
      return null;
    }
    return value > 0 ? 'gain' : 'loss';
  }

  private static signed(value: number): string {
    return `${value > 0 ? '+' : ''}${RecommendationUtils.number(value, PCT_FRACTION_DIGITS)}`;
  }

  /** "SELL 348 GRAB / LIMIT 3.13 USD DAY / IBKR" as its three lines: the order, the price terms, the venue. */
  public static orderLines(orderText: Nullable<string>): string[] {
    return orderText
      ? orderText
          .split(ORDER_SEPARATOR)
          .map(line => line.trim())
          .filter(line => line !== '')
      : [];
  }

  /** Oldest first, the order the queue works through. */
  public static oldestFirst(items: readonly RecommendationDto[]): RecommendationDto[] {
    return [...items].sort(
      (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
    );
  }

  private static pct(value: Nullable<number>): string {
    return value === null ? '' : `${RecommendationUtils.number(value, PCT_FRACTION_DIGITS)}%`;
  }

  private static shares(value: Nullable<number>): string {
    return value === null || value <= 0
      ? ''
      : new Intl.NumberFormat(EN_US, {maximumFractionDigits: SHARE_FRACTION_DIGITS}).format(value);
  }

  private static aboutUsd(value: number): string {
    return MoneyUtils.format(value, 'USD', {maxFractionDigits: WHOLE_DOLLARS});
  }

  private static number(value: number, digits: number): string {
    return new Intl.NumberFormat(EN_US, {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    }).format(value);
  }
}
