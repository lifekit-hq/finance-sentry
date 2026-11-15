import {type DossierSectionChip} from '../../models/dossier/dossier.model';

/** Anchor ids of the dossier sections; the sticky chip bar and alert fragments scroll to them. */
export const DOSSIER_SECTION_ID = {
  checks: 'checks',
  chart: 'price-chart',
  interactiveChart: 'interactive-chart',
  returns: 'returns',
  valuation: 'valuation',
  analysts: 'analyst-coverage',
  thesis: 'thesis',
  earnings: 'next-earnings',
  moves: 'why-it-moved',
  news: 'recent-news',
  signals: 'radar-signals',
  ownership: 'ownership',
  dataStatus: 'data-status',
} as const;

/** The anchor of the tile grid the fact sections live in; a chip or an alert fragment scrolls to it. */
export const DOSSIER_FACTS_ID = 'dossier-facts';

/** A notable move is a tile of its own; its id is this prefix and the move's ISO date. */
export const MOVE_TILE_ID_PREFIX = 'move-';

/** The fact tiles, in grid order; each id is the section id the section had as a card, so an alert fragment still lands. */
export const FACT_TILE_ORDER = [
  DOSSIER_SECTION_ID.valuation,
  DOSSIER_SECTION_ID.analysts,
  DOSSIER_SECTION_ID.thesis,
  DOSSIER_SECTION_ID.earnings,
  DOSSIER_SECTION_ID.news,
  DOSSIER_SECTION_ID.signals,
  DOSSIER_SECTION_ID.ownership,
] as const;

export const VALUATION_CARD_TITLE = 'Is it cheap?';
export const CRYPTO_PRICE_CARD_TITLE = 'Price & your target';
export const VALUATION_TILE_LABEL = 'Valuation';
export const CRYPTO_PRICE_TILE_LABEL = 'Price & target';
export const MOVE_TILE_LABEL = 'Moved';

export const DOSSIER_SECTION_CHIPS = {
  checks: {id: DOSSIER_SECTION_ID.checks, label: 'Checks'},
  chart: {id: DOSSIER_SECTION_ID.chart, label: 'Chart'},
  returns: {id: DOSSIER_SECTION_ID.returns, label: 'Returns'},
  valuation: {id: DOSSIER_SECTION_ID.valuation, label: 'Valuation'},
  cryptoPrice: {id: DOSSIER_SECTION_ID.valuation, label: CRYPTO_PRICE_CARD_TITLE},
  analysts: {id: DOSSIER_SECTION_ID.analysts, label: 'Analysts'},
  thesis: {id: DOSSIER_SECTION_ID.thesis, label: 'Plan'},
  earnings: {id: DOSSIER_SECTION_ID.earnings, label: 'Earnings'},
  // A move is a tile each; the chip opens the newest one (the section builder supplies its id).
  moves: {id: DOSSIER_SECTION_ID.moves, label: 'Moves'},
  news: {id: DOSSIER_SECTION_ID.news, label: 'News'},
  signals: {id: DOSSIER_SECTION_ID.signals, label: 'Signals'},
  dataStatus: {id: DOSSIER_SECTION_ID.dataStatus, label: 'Data'},
} as const satisfies Record<string, DossierSectionChip>;

export const NO_LINKED_MOVE_ITEMS_MESSAGE = 'no linked news or signal found';
