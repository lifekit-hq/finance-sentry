import {formatDate} from '@angular/common';
import {type CmnTile, type Nullable} from '@lifekit-hq/ui';

import {APP_LOCALE} from '../../../shared/constants/date/date.constants';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  CRYPTO_PRICE_TILE_LABEL,
  DOSSIER_SECTION_ID,
  FACT_TILE_ORDER,
  MOVE_TILE_ID_PREFIX,
  MOVE_TILE_LABEL,
  VALUATION_TILE_LABEL,
} from '../constants/dossier/dossier-sections.constants';
import {
  DEFAULT_TILE_HEADLINE,
  MOVE_CAUSE_NEWS,
  MOVE_CAUSE_NONE,
  MOVE_CAUSE_SIGNAL,
  MOVE_TILE_DATE_FORMAT,
  TILE_HEADLINE,
} from '../constants/dossier/dossier-tiles.constants';
import {
  type AssetDossierDto,
  type DossierNotableMoveDto,
  type RecommendationSummary,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {type DossierMoveView} from '../models/dossier/dossier-tiles.model';
import {type TargetBandView} from '../models/dossier/dossier-view.model';
import {DossierFactsUtils} from './dossier-facts.utils';
import {DossierViewUtils} from './dossier-view.utils';

const PERCENT_SCALE = 100;
const CAUSE_SEPARATOR = ', ';

/** What the fact tiles are built from; every field is a derivation the store already holds. */
export interface FactTileInput {
  dossier: AssetDossierDto;
  valuation: Nullable<ValuationSnapshotDto>;
  targetBand: Nullable<TargetBandView>;
  recommendation: Nullable<RecommendationSummary>;
}

export class DossierTilesUtils {
  /** True for any id the grid can carry: an alert fragment names a card by it, so it opens a tile instead. */
  public static isTileId(id: string): boolean {
    const sectionIds: readonly string[] = FACT_TILE_ORDER;
    return sectionIds.includes(id) || id.startsWith(MOVE_TILE_ID_PREFIX);
  }

  /** The notable moves of the visible window, newest first, each with its news and signal counts. */
  public static moves(dossier: Nullable<AssetDossierDto>): DossierMoveView[] {
    return (dossier?.notableMoves?.moves ?? []).map(move => DossierTilesUtils.moveView(move));
  }

  /**
   * One-line facts for the grid, in page order. A section with nothing to say gets no tile, as it got
   * no card; the moves follow the facts, one tile each, newest first.
   */
  public static tiles(input: FactTileInput): CmnTile[] {
    const {dossier} = input;
    const isCrypto = DossierViewUtils.isCrypto(dossier);
    const tiles: CmnTile[] = [];

    if (DossierViewUtils.hasValuationCard(dossier)) {
      tiles.push({
        id: DOSSIER_SECTION_ID.valuation,
        label: isCrypto ? CRYPTO_PRICE_TILE_LABEL : VALUATION_TILE_LABEL,
        headline: DossierTilesUtils.valuationHeadline(input),
      });
    }
    if (DossierViewUtils.hasAnalysts(dossier) && dossier.analysts) {
      tiles.push({
        id: DOSSIER_SECTION_ID.analysts,
        label: TILE_HEADLINE.analystsLabel,
        headline: input.recommendation
          ? `${input.recommendation.verdictCount} of ${input.recommendation.total} ${input.recommendation.verdict}`
          : DossierTilesUtils.count(dossier.analysts.recentActions.length, 'action', 'actions'),
      });
    }
    if (dossier.thesis) {
      tiles.push({
        id: DOSSIER_SECTION_ID.thesis,
        label: TILE_HEADLINE.thesisLabel,
        headline: dossier.thesis.brokenAt ? TILE_HEADLINE.thesisBroken : TILE_HEADLINE.thesisActive,
      });
    }
    if (dossier.nextEarnings) {
      tiles.push({
        id: DOSSIER_SECTION_ID.earnings,
        label: TILE_HEADLINE.earningsLabel,
        headline: dossier.nextEarnings.eventDate,
      });
    }
    if (dossier.recentNews.length > 0) {
      tiles.push({
        id: DOSSIER_SECTION_ID.news,
        label: TILE_HEADLINE.newsLabel,
        headline: DossierTilesUtils.count(dossier.recentNews.length, 'article', 'articles'),
      });
    }
    if (dossier.radarSignals.length > 0) {
      tiles.push({
        id: DOSSIER_SECTION_ID.signals,
        label: TILE_HEADLINE.signalsLabel,
        headline: DossierTilesUtils.count(dossier.radarSignals.length, 'signal', 'signals'),
      });
    }
    if (!isCrypto) {
      tiles.push({
        id: DOSSIER_SECTION_ID.ownership,
        label: TILE_HEADLINE.ownershipLabel,
        headline: TILE_HEADLINE.ownershipUnavailable,
      });
    }
    for (const move of DossierTilesUtils.moves(dossier)) {
      tiles.push({
        id: move.tileId,
        label: `${MOVE_TILE_LABEL} ${formatDate(move.date, MOVE_TILE_DATE_FORMAT, APP_LOCALE)}`,
        headline: DossierTilesUtils.moveHeadline(move),
      });
    }
    return tiles;
  }

  /** The analysts' target with the room to it, else the owner's own target, else the forward multiple. */
  private static valuationHeadline(input: FactTileInput): string {
    const band = input.targetBand;
    const analyst = band?.markers.find(marker => marker.key === 'analyst');
    const price = band?.markers.find(marker => marker.key === 'price');
    if (band && analyst && price && price.value > 0) {
      const room = DossierFactsUtils.signedPercent(
        ((analyst.value - price.value) / price.value) * PERCENT_SCALE
      );
      return `${MoneyUtils.format(analyst.value, band.currency)} · ${room}`;
    }
    const mine = band?.markers.find(marker => marker.key === 'mine');
    if (band && mine) {
      return `${TILE_HEADLINE.yourTarget} ${MoneyUtils.format(mine.value, band.currency)}`;
    }
    const forwardPe = input.valuation?.metrics.forwardPe.value ?? null;
    if (forwardPe !== null) {
      return `${TILE_HEADLINE.forwardPe} ${forwardPe.toFixed(1)}x`;
    }
    return DEFAULT_TILE_HEADLINE.valuation;
  }

  private static moveView(move: DossierNotableMoveDto): DossierMoveView {
    return {
      tileId: DossierViewUtils.moveTileId(move.date),
      date: move.date,
      returnPct: move.returnPct,
      direction: move.returnPct >= 0 ? 'up' : 'down',
      newsCount: move.links.filter(link => link.kind === 'news').length,
      signalCount: move.links.filter(link => link.kind === 'signal').length,
      links: move.links,
    };
  }

  /** "+8.2% · news, signal": the size of the move, then what the held data puts around it. */
  private static moveHeadline(move: DossierMoveView): string {
    const causes = [
      move.newsCount > 0 ? MOVE_CAUSE_NEWS : null,
      move.signalCount > 0 ? MOVE_CAUSE_SIGNAL : null,
    ].filter((cause): cause is string => cause !== null);
    const size = DossierFactsUtils.signedPercent(move.returnPct) ?? '';
    return `${size} · ${causes.length > 0 ? causes.join(CAUSE_SEPARATOR) : MOVE_CAUSE_NONE}`;
  }

  private static count(n: number, one: string, many: string): string {
    return `${n} ${n === 1 ? one : many}`;
  }
}
