import {DateUtils} from '../../../shared/utils/date.utils';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  FUNDAMENTAL_REASONS,
  MARKET_CAP_UNITS,
  OWNERSHIP_NOTE,
} from '../constants/dossier/dossier-facts.constants';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type DossierPerformanceDto,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {
  type DataStatusRow,
  type FundamentalCell,
  type PerformanceStripItem,
} from '../models/dossier/dossier-view.model';
import {DossierLabelUtils} from './dossier-label.utils';
import {DossierViewUtils} from './dossier-view.utils';

const PERCENT_DIGITS = 1;
const MULTIPLE_DIGITS = 1;
const CAP_DIGITS = 2;
const LIST_SEPARATOR = ', ';

export class DossierFactsUtils {
  /** "+22.2%" / "-2.0%" / "0.0%"; null stays null. */
  public static signedPercent(
    value: Nullable<number> | undefined,
    digits = PERCENT_DIGITS
  ): Nullable<string> {
    if (value === null || value === undefined || !Number.isFinite(value)) {
      return null;
    }
    const rounded = Number(value.toFixed(digits));
    const sign = rounded > 0 ? '+' : rounded < 0 ? '-' : '';
    return `${sign}${Math.abs(rounded).toFixed(digits)}%`;
  }

  public static tone(value: Nullable<number> | undefined): Nullable<'gain' | 'loss'> {
    if (value === null || value === undefined || value === 0 || !Number.isFinite(value)) {
      return null;
    }
    return value > 0 ? 'gain' : 'loss';
  }

  /** The #824 strip: "year +22.2%", "from 52w high -2.0%", "S&P 500 +14.1%"; facts without a value are left out. */
  public static performanceStrip(
    performance: Nullable<DossierPerformanceDto> | undefined
  ): PerformanceStripItem[] {
    if (!performance) {
      return [];
    }
    const items: PerformanceStripItem[] = [];
    const push = (key: string, label: string, value: Nullable<number>) => {
      const text = DossierFactsUtils.signedPercent(value);
      if (text !== null) {
        items.push({key, label, value: text, tone: DossierFactsUtils.tone(value)});
      }
    };
    push('year', 'year', performance.yearChangePct);
    push('from-high', 'from 52w high', performance.fromHighPct);
    push('benchmark', 'S&P 500', performance.benchmarkYearChangePct);
    return items;
  }

  /**
   * One line on why this symbol matters to the book: "Held · 12.5% of book"; for a name not held,
   * its next earnings or its latest radar signal. Null when there is nothing to say.
   */
  public static thesisHook(dossier: Nullable<AssetDossierDto>): Nullable<string> {
    if (!dossier) {
      return null;
    }
    if (dossier.position) {
      const share = dossier.position.shareOfBookPercent;
      return share === null ? 'Held' : `Held · ${share.toFixed(PERCENT_DIGITS)}% of book`;
    }
    if (dossier.nextEarnings) {
      return `Earnings ${DateUtils.dayLabel(dossier.nextEarnings.eventDate)}`;
    }
    const latest = [...dossier.radarSignals].sort((a, b) =>
      b.timestamp.localeCompare(a.timestamp)
    )[0];
    return latest ? `Signal · ${DossierLabelUtils.signalType(latest.signalType)}` : null;
  }

  /** "$3.12T", "€812.40B": market cap in its quote currency. */
  public static marketCap(
    value: Nullable<number> | undefined,
    currency: Nullable<string> | undefined
  ): Nullable<string> {
    if (value === null || value === undefined || !Number.isFinite(value) || value <= 0) {
      return null;
    }
    const unit = MARKET_CAP_UNITS.find(u => value >= u.size);
    if (!unit) {
      return MoneyUtils.format(value, currency, {maxFractionDigits: 0});
    }
    return `${MoneyUtils.format(value / unit.size, currency, {maxFractionDigits: CAP_DIGITS})}${unit.suffix}`;
  }

  /**
   * The scannable two-column snapshot (#824). A missing value carries the reason it is missing,
   * never a zero.
   */
  public static fundamentals(
    valuation: Nullable<ValuationSnapshotDto>,
    performance: Nullable<DossierPerformanceDto> | undefined
  ): FundamentalCell[] {
    if (!valuation || valuation.notApplicable) {
      return [];
    }
    const currency = valuation.currency ?? performance?.currency ?? null;
    const multiple = (value: Nullable<number>) =>
      value === null || !Number.isFinite(value) || value <= 0
        ? null
        : `${value.toFixed(MULTIPLE_DIGITS)}x`;
    const price = (value: Nullable<number> | undefined) =>
      value === null || value === undefined ? null : MoneyUtils.format(value, currency);
    const cell = (
      key: string,
      label: string,
      value: Nullable<string>,
      reason: string,
      tone: Nullable<'gain' | 'loss'> = null
    ): FundamentalCell => ({
      key,
      label,
      value,
      reason: value === null ? reason : null,
      tone: value === null ? null : tone,
    });

    const target =
      valuation.consensusTarget === null
        ? null
        : [
            price(valuation.consensusTarget),
            DossierFactsUtils.signedPercent(valuation.impliedUpsidePct),
          ]
            .filter((part): part is string => part !== null)
            .join(' · ');

    return [
      cell(
        'market-cap',
        'Market cap',
        DossierFactsUtils.marketCap(valuation.marketCap, currency),
        FUNDAMENTAL_REASONS.notReported
      ),
      cell(
        'forward-pe',
        'P/E (next 12 months)',
        multiple(valuation.metrics.forwardPe.value),
        FUNDAMENTAL_REASONS.noEstimate
      ),
      cell(
        'trailing-pe',
        'P/E (trailing)',
        multiple(valuation.metrics.trailingPe.value),
        FUNDAMENTAL_REASONS.noEarnings
      ),
      cell(
        'ev-ebitda',
        'EV/EBITDA',
        multiple(valuation.metrics.evToEbitda.value),
        FUNDAMENTAL_REASONS.notReported
      ),
      cell(
        'dividend-yield',
        'Dividend yield',
        DossierViewUtils.dividendYield(valuation.metrics.dividendYield.value),
        FUNDAMENTAL_REASONS.noDividend
      ),
      cell(
        'year-change',
        'Change over a year',
        DossierFactsUtils.signedPercent(performance?.yearChangePct),
        FUNDAMENTAL_REASONS.noHistory,
        DossierFactsUtils.tone(performance?.yearChangePct)
      ),
      cell(
        'high-52w',
        '52-week high',
        price(performance?.high52Week),
        FUNDAMENTAL_REASONS.noHistory
      ),
      cell('low-52w', '52-week low', price(performance?.low52Week), FUNDAMENTAL_REASONS.noHistory),
      cell(
        'from-high',
        'From 52-week high',
        DossierFactsUtils.signedPercent(performance?.fromHighPct),
        FUNDAMENTAL_REASONS.noHistory,
        DossierFactsUtils.tone(performance?.fromHighPct)
      ),
      cell(
        'analyst-target',
        'Analyst target',
        target,
        FUNDAMENTAL_REASONS.noTarget,
        DossierFactsUtils.tone(valuation.impliedUpsidePct)
      ),
    ];
  }

  /** Where every section came from and when it was read; a section that cannot be fed says so. */
  public static dataStatus(
    dossier: Nullable<AssetDossierDto>,
    chart: Nullable<AssetChartDto>
  ): DataStatusRow[] {
    if (!dossier) {
      return [];
    }
    const rows: DataStatusRow[] = [
      {
        key: 'dossier',
        label: 'Dossier',
        source: 'Finance Sentry',
        at: dossier.generatedAt,
        note: null,
      },
    ];
    const isCrypto = DossierViewUtils.isCrypto(dossier);
    const valuation = dossier.valuation && !isCrypto ? dossier.valuation : null;
    if (valuation) {
      rows.push({
        key: 'valuation',
        label: 'Price and valuation',
        source: DossierFactsUtils.sources(valuation.sources),
        at: valuation.priceAsOf ?? valuation.retrievedAt,
        note: valuation.isStale ? 'stale' : null,
      });
    }
    if (chart && chart.points.length > 0) {
      rows.push({
        key: 'chart',
        label: 'Price history',
        source: DossierLabelUtils.dataSource(chart.source),
        at: chart.retrievedAt,
        note: null,
      });
    }
    const trends = dossier.analysts?.trends ?? [];
    if (trends.length > 0) {
      const latest = [...trends].sort((a, b) => b.ingestedAt.localeCompare(a.ingestedAt))[0];
      rows.push({
        key: 'analysts',
        label: 'Analyst ratings',
        source: DossierLabelUtils.dataSource(latest.source),
        at: latest.ingestedAt,
        note: null,
      });
    }
    if (dossier.recentNews.length > 0) {
      const latest = [...dossier.recentNews].sort((a, b) =>
        b.publishedAt.localeCompare(a.publishedAt)
      )[0];
      rows.push({
        key: 'news',
        label: 'News',
        source: DossierFactsUtils.distinct(
          dossier.recentNews.map(n => DossierLabelUtils.newsSource(n.source))
        ),
        at: latest.publishedAt,
        note: null,
      });
    }
    const returns = dossier.position?.returns ?? null;
    if (returns) {
      rows.push({
        key: 'returns',
        label: 'Return decomposition',
        source: 'Broker fills',
        at: null,
        note: `${returns.fillCount} ${returns.fillCount === 1 ? 'fill' : 'fills'} · ${returns.coverage}`,
      });
    }
    if (!isCrypto) {
      rows.push({
        key: 'ownership',
        label: 'Ownership',
        source: null,
        at: null,
        note: OWNERSHIP_NOTE,
      });
    }
    return rows;
  }

  /** Distinct provider names of provenance codes, in first-seen order. */
  public static sources(codes: readonly string[]): string {
    return DossierFactsUtils.distinct(codes.map(code => DossierLabelUtils.dataSource(code)));
  }

  /** Distinct names joined in first-seen order; "-" when there are none. */
  public static distinct(names: readonly string[]): string {
    const unique = [...new Set(names)];
    return unique.length > 0 ? unique.join(LIST_SEPARATOR) : '-';
  }
}
