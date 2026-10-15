import {
  HEADLINE_BADGE_LABEL,
  HEADLINE_UNAVAILABLE_TEXT,
  STALE_SOURCE_CODE,
} from '../constants/headline/headline.constants';
import {
  type Headline,
  type HeadlineBadge,
  type HeadlineBadgeKind,
  type HeadlineQuality,
  type HeadlineReason,
  type MaybeHeadline,
} from '../models/headline/headline.model';
import {ProviderUtils} from './provider.utils';

const QUALITY_RANK: Readonly<Record<HeadlineQuality, number>> = {
  COMPLETE: 0,
  ESTIMATED: 1,
  DEGRADED: 2,
  UNAVAILABLE: 3,
};

const PERCENT_SCALE = 100;

export class HeadlineUtils {
  /**
   * The badge a figure wears, or null when it needs none: complete, or no headline at all (an
   * older API build). Degraded is "Stale" when a source is behind it and "Partial" otherwise
   * (a currency passed through 1:1, a non-cash item counted in).
   */
  public static badge(headline: MaybeHeadline): Nullable<HeadlineBadge> {
    if (!headline || headline.quality === 'COMPLETE') {
      return null;
    }
    return HeadlineUtils.badgeOf(HeadlineUtils.kind(headline));
  }

  /** True only for a headline that says it has no figure; a missing headline is not unavailable. */
  public static isUnavailable(headline: MaybeHeadline): boolean {
    return headline?.quality === 'UNAVAILABLE';
  }

  /** `N/A` for an unavailable figure, else `formatted`; an unavailable figure is never 0. */
  public static display(headline: MaybeHeadline, formatted: string): string {
    return HeadlineUtils.isUnavailable(headline) ? HEADLINE_UNAVAILABLE_TEXT : formatted;
  }

  /**
   * One headline for a figure shown as the sum of several (the cash and invested split lines, the
   * brokerage and crypto holdings): quality the worst of its parts, reasons the union, `asOf` the
   * oldest. A part with no amount is left out of the sum and caps the whole at degraded, so one
   * missing sleeve reads "Partial"; unavailable only when every part is. Null when none is known.
   */
  public static combine(parts: MaybeHeadline[]): Nullable<Headline> {
    const known = parts.filter((p): p is Headline => !!p);
    if (known.length === 0) {
      return null;
    }
    const measured = known.filter(p => !HeadlineUtils.isUnavailable(p));
    const worst = measured.reduce<HeadlineQuality>(
      (acc, p) => (QUALITY_RANK[p.quality] > QUALITY_RANK[acc] ? p.quality : acc),
      'COMPLETE'
    );
    const quality: HeadlineQuality =
      measured.length === 0
        ? 'UNAVAILABLE'
        : HeadlineUtils.capped(worst, measured.length < known.length);
    const coverages = known.map(p => p.coverage).filter((c): c is number => c != null);
    const asOfs = known.map(p => p.asOf).filter((a): a is string => !!a);
    return {
      ...known[0],
      amount: measured.length === 0 ? null : measured.reduce((sum, p) => sum + (p.amount ?? 0), 0),
      percent: known.length === 1 ? known[0].percent : null,
      quality,
      coverage: coverages.length === 0 ? null : Math.min(...coverages),
      reasons: HeadlineUtils.mergeReasons(known.flatMap(p => p.reasons)),
      asOf: asOfs.length === 0 ? null : asOfs.reduce((a, b) => (a <= b ? a : b)),
    };
  }

  /**
   * `headline` with the sources whose read failed added as `STALE_SOURCE` reasons, so a total that
   * silently lacks a broken connection says so. With no headline to extend (an older API build),
   * one is built around `amount`: degraded when the figure is known, unavailable when it is not.
   * Returns `headline` itself when nothing failed.
   */
  public static withStaleSources(
    headline: MaybeHeadline,
    sources: string[],
    currency: string,
    amount: Nullable<number> = null
  ): Nullable<Headline> {
    if (sources.length === 0) {
      return headline ?? null;
    }
    const added: HeadlineReason[] = sources.map(source => ({code: STALE_SOURCE_CODE, source}));
    if (!headline) {
      return {
        amount,
        percent: null,
        method: 'VALUE',
        scope: 'umbrella',
        window: {to: ''},
        quality: amount === null ? 'UNAVAILABLE' : 'DEGRADED',
        reasons: added,
        currency,
      };
    }
    return {
      ...headline,
      quality: HeadlineUtils.capped(headline.quality, true),
      reasons: HeadlineUtils.mergeReasons([...headline.reasons, ...added]),
    };
  }

  /** "Banking", "Binance": a provider's display name, else the slug capitalised. */
  public static sourceLabel(source: string): string {
    const label = ProviderUtils.label(source);
    return label === source ? source.charAt(0).toUpperCase() + source.slice(1) : label;
  }

  /** "80%" for a 0..1 coverage. */
  public static coverageLabel(coverage: number): string {
    return `${Math.round(coverage * PERCENT_SCALE)}%`;
  }

  private static kind(headline: Headline): HeadlineBadgeKind {
    switch (headline.quality) {
      case 'ESTIMATED':
        return 'estimated';
      case 'UNAVAILABLE':
        return 'unavailable';
      default:
        return headline.reasons.some(r => r.code === STALE_SOURCE_CODE) ? 'stale' : 'partial';
    }
  }

  private static badgeOf(kind: HeadlineBadgeKind): HeadlineBadge {
    return {kind, label: HEADLINE_BADGE_LABEL[kind]};
  }

  /** At least degraded when `degrade` (a part is missing), else `quality` unchanged. */
  private static capped(quality: HeadlineQuality, degrade: boolean): HeadlineQuality {
    return degrade && QUALITY_RANK[quality] < QUALITY_RANK.DEGRADED ? 'DEGRADED' : quality;
  }

  private static mergeReasons(reasons: HeadlineReason[]): HeadlineReason[] {
    const seen = new Set<string>();
    return reasons.filter(r => {
      const key = `${r.code}|${r.source ?? ''}`;
      if (seen.has(key)) {
        return false;
      }
      seen.add(key);
      return true;
    });
  }
}
