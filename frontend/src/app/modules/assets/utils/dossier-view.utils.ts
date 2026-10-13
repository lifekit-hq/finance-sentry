import {
  RECOMMENDATION_BUCKETS,
  RECOMMENDATION_BUY_LABEL,
  RECOMMENDATION_HOLD_LABEL,
  RECOMMENDATION_SELL_LABEL,
} from '../constants/dossier/dossier-recommendation.constants';
import {DOSSIER_SECTION_CHIPS} from '../constants/dossier/dossier-sections.constants';
import {
  type AssetDossierDto,
  type DossierHeroProfile,
  type DossierSectionChip,
  type RecommendationSegment,
  type RecommendationSummary,
  type RecommendationTrendDto,
} from '../models/dossier/dossier.model';

const PERCENT_SCALE = 100;
const YIELD_FRACTION_DIGITS = 2;
const DETAIL_SEPARATOR = ' · ';
const CRYPTO_ASSET_CLASS = 'crypto';

export class DossierViewUtils {
  /**
   * The newest month of the recommendation trend as one verdict plus the bar's slices.
   * Null when there is no month or nobody covered the name.
   */
  public static recommendationSummary(
    trends: readonly RecommendationTrendDto[]
  ): Nullable<RecommendationSummary> {
    if (trends.length === 0) {
      return null;
    }
    const latest = [...trends].sort(
      (a, b) => new Date(b.period).getTime() - new Date(a.period).getTime()
    )[0];
    const total = RECOMMENDATION_BUCKETS.reduce((sum, b) => sum + latest[b.bucket], 0);
    if (total <= 0) {
      return null;
    }

    const segments: RecommendationSegment[] = RECOMMENDATION_BUCKETS.filter(
      b => latest[b.bucket] > 0
    ).map(b => ({
      bucket: b.bucket,
      label: b.label,
      count: latest[b.bucket],
      sharePct: (latest[b.bucket] / total) * PERCENT_SCALE,
      barClass: b.barClass,
    }));

    const sides = [
      {label: RECOMMENDATION_BUY_LABEL, count: latest.strongBuy + latest.buy},
      {label: RECOMMENDATION_HOLD_LABEL, count: latest.hold},
      {label: RECOMMENDATION_SELL_LABEL, count: latest.sell + latest.strongSell},
    ];
    // The first of equals wins, so a tie reads as the bullish side.
    const winner = sides.reduce((best, side) => (side.count > best.count ? side : best));

    return {
      period: latest.period,
      total,
      verdict: winner.label,
      verdictCount: winner.count,
      segments,
    };
  }

  /**
   * Crypto has no valuation, analyst or ownership data. The same rule as the backend: the holding's asset
   * class, or a valuation the source reported as not applicable (an unheld coin).
   */
  public static isCrypto(dossier: Nullable<AssetDossierDto>): boolean {
    return (
      dossier?.position?.assetClass?.toLowerCase() === CRYPTO_ASSET_CLASS ||
      dossier?.valuation?.notApplicable === true
    );
  }

  /** Company name and "Sector · Industry" from the valuation source; a holding falls back to its asset class. */
  public static heroProfile(dossier: AssetDossierDto): DossierHeroProfile {
    const valuation =
      dossier.valuation && !dossier.valuation.notApplicable ? dossier.valuation : null;
    const name = valuation?.name?.trim() || null;
    const detail = [valuation?.sector, valuation?.industry]
      .map(part => part?.trim())
      .filter((part): part is string => !!part)
      .join(DETAIL_SEPARATOR);
    return {name, detail: detail || dossier.position?.assetClass || null};
  }

  /** The source reports yield as a fraction (0.0295): "2.95%". */
  public static dividendYield(fraction: Nullable<number>): Nullable<string> {
    if (fraction === null || !Number.isFinite(fraction)) {
      return null;
    }
    return `${(fraction * PERCENT_SCALE).toFixed(YIELD_FRACTION_DIGITS)}%`;
  }

  /** The sections that exist for this symbol, in page order: an absent section gets no chip. */
  public static sectionChips(dossier: Nullable<AssetDossierDto>): DossierSectionChip[] {
    if (!dossier) {
      return [];
    }
    const chips: DossierSectionChip[] = [];
    const hasHistory = (dossier.performance ?? null) !== null;
    const hasValuation = dossier.valuation !== null && !dossier.valuation.notApplicable;
    if ((dossier.checks?.axes.length ?? 0) > 0) {
      chips.push(DOSSIER_SECTION_CHIPS.checks);
    }
    if (hasHistory) {
      chips.push(DOSSIER_SECTION_CHIPS.chart);
    }
    if (dossier.position?.returns) {
      chips.push(DOSSIER_SECTION_CHIPS.returns);
    }
    // "Is it cheap?" also carries the user's own target, so a priced asset without a valuation keeps it.
    if (hasValuation || hasHistory) {
      chips.push(DOSSIER_SECTION_CHIPS.valuation);
    }
    if (dossier.analysts) {
      chips.push(DOSSIER_SECTION_CHIPS.analysts);
    }
    if (dossier.thesis) {
      chips.push(DOSSIER_SECTION_CHIPS.thesis);
    }
    if (dossier.nextEarnings) {
      chips.push(DOSSIER_SECTION_CHIPS.earnings);
    }
    if (dossier.recentNews.length > 0) {
      chips.push(DOSSIER_SECTION_CHIPS.news);
    }
    if (dossier.radarSignals.length > 0) {
      chips.push(DOSSIER_SECTION_CHIPS.signals);
    }
    chips.push(DOSSIER_SECTION_CHIPS.dataStatus);
    return chips;
  }
}
