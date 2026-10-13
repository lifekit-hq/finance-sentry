import {
  ANALYST_TARGET_SOURCE,
  CAP_SCALE_HEADROOM,
  MAX_TARGET_PRICE,
  RETURN_LEG_LABELS,
  RETURNS_COVERAGE_NOTES,
  TARGET_SCALE_PADDING,
  TARGET_TEXT,
} from '../constants/dossier/dossier-position.constants';
import {
  type DossierPositionSection,
  type DossierUserTargetDto,
  type PositionReturnsDto,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {
  type DossierReturnsView,
  type ReturnLegView,
  type SizeCapView,
  type TargetBandView,
  type TargetMarker,
} from '../models/dossier/dossier-view.model';
import {DossierLabelUtils} from './dossier-label.utils';

const FULL_PCT = 100;
const CENTRE_PCT = 50;
const DEFAULT_CURRENCY = 'USD';

export class DossierPositionUtils {
  /** The four legs of the return with bars scaled to the largest; null without a decomposition. */
  public static returns(
    returns: Nullable<PositionReturnsDto> | undefined
  ): Nullable<DossierReturnsView> {
    if (!returns) {
      return null;
    }
    const values: Record<ReturnLegView['key'], Nullable<number>> = {
      unrealized: returns.unrealizedUsd,
      realized: returns.realizedUsd,
      dividends: returns.dividendsUsd,
      currency: returns.currencyUsd,
    };
    const largest = Math.max(0, ...Object.values(values).map(v => (v === null ? 0 : Math.abs(v))));
    const legs: ReturnLegView[] = (Object.keys(values) as ReturnLegView['key'][]).map(key => {
      const value = values[key];
      return {
        key,
        label: RETURN_LEG_LABELS[key],
        value,
        barPct: value === null || largest === 0 ? 0 : (Math.abs(value) / largest) * FULL_PCT,
      };
    });
    const note =
      returns.coverage === 'complete'
        ? `${RETURNS_COVERAGE_NOTES.complete} (${returns.fillCount} ${returns.fillCount === 1 ? 'fill' : 'fills'}).`
        : RETURNS_COVERAGE_NOTES[returns.coverage];
    return {legs, total: returns.totalUsd, note};
  }

  /** The position's share of the book against the IPS cap; the scale leaves room past the larger. */
  public static sizeCap(position: Nullable<DossierPositionSection>): Nullable<SizeCapView> {
    const share = position?.shareOfBookPercent ?? null;
    if (share === null || share < 0) {
      return null;
    }
    const cap = position?.positionCapPercent ?? null;
    const scale = Math.max(share, cap ?? 0) * CAP_SCALE_HEADROOM;
    if (scale <= 0) {
      return {sharePct: share, capPct: cap, fillPct: 0, capMarkPct: null, overCap: false};
    }
    return {
      sharePct: share,
      capPct: cap,
      fillPct: (share / scale) * FULL_PCT,
      capMarkPct: cap === null ? null : (cap / scale) * FULL_PCT,
      overCap: cap !== null && share > cap,
    };
  }

  /**
   * "Is it cheap?": the price against the analysts' low-to-high range, their mean and the user's own
   * target, on one scale padded past the extremes. Null when there is nothing to compare the price with.
   */
  public static targetBand(
    valuation: Nullable<ValuationSnapshotDto>,
    userTarget: Nullable<DossierUserTargetDto> | undefined,
    price: Nullable<number>
  ): Nullable<TargetBandView> {
    const usable = valuation && !valuation.notApplicable ? valuation : null;
    const low = DossierPositionUtils.positive(usable?.targetLow);
    const high = DossierPositionUtils.positive(usable?.targetHigh);
    const mean = DossierPositionUtils.positive(usable?.consensusTarget);
    const mine = DossierPositionUtils.positive(userTarget?.targetPrice);
    const current = DossierPositionUtils.positive(price);
    const hasRange = low !== null && high !== null && high >= low;
    if (current === null || (!hasRange && mean === null && mine === null)) {
      return null;
    }

    const known = [current, mean, mine, hasRange ? low : null, hasRange ? high : null].filter(
      (v): v is number => v !== null
    );
    const min = Math.min(...known);
    const max = Math.max(...known);
    const padding = (max - min) * TARGET_SCALE_PADDING || min * TARGET_SCALE_PADDING;
    const from = Math.max(0, min - padding);
    const span = max + padding - from;
    const at = (value: number) => (span > 0 ? ((value - from) / span) * FULL_PCT : CENTRE_PCT);

    const markers: TargetMarker[] = [
      {key: 'price', label: 'Price', value: current, leftPct: at(current)},
    ];
    if (mean !== null) {
      markers.push({key: 'analyst', label: 'Analyst target', value: mean, leftPct: at(mean)});
    }
    if (mine !== null) {
      markers.push({key: 'mine', label: 'Your target', value: mine, leftPct: at(mine)});
    }

    return {
      currency: usable?.currency ?? userTarget?.currency ?? DEFAULT_CURRENCY,
      low: hasRange ? low : null,
      high: hasRange ? high : null,
      lowPct: hasRange ? at(low) : null,
      highPct: hasRange ? at(high) : null,
      markers,
      source: mean === null && !hasRange ? null : DossierPositionUtils.targetSource(usable),
    };
  }

  /** "Yahoo Finance · 38 analysts": the analyst target always names where it came from. */
  public static targetSource(valuation: Nullable<ValuationSnapshotDto>): string {
    const provider = DossierLabelUtils.dataSource(ANALYST_TARGET_SOURCE);
    const count = valuation?.analystCount ?? null;
    if (count === null || count <= 0) {
      return provider;
    }
    return `${provider} · ${count} ${count === 1 ? 'analyst' : 'analysts'}`;
  }

  /** The editor's text as a target price: a positive number below the API's ceiling, else null. */
  public static parseTarget(text: string): Nullable<number> {
    const trimmed = text.trim();
    if (!TARGET_TEXT.test(trimmed)) {
      return null;
    }
    const value = Number(trimmed.replaceAll(',', ''));
    return Number.isFinite(value) && value > 0 && value <= MAX_TARGET_PRICE ? value : null;
  }

  private static positive(value: Nullable<number> | undefined): Nullable<number> {
    return value !== null && value !== undefined && Number.isFinite(value) && value > 0
      ? value
      : null;
  }
}
