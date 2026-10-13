import {type DossierSectionChip} from '../../models/dossier/dossier.model';

/** Anchor ids of the dossier sections; the sticky chip bar and alert fragments scroll to them. */
export const DOSSIER_SECTION_ID = {
  checks: 'checks',
  chart: 'price-chart',
  returns: 'returns',
  valuation: 'valuation',
  analysts: 'analyst-coverage',
  thesis: 'thesis',
  earnings: 'next-earnings',
  news: 'recent-news',
  signals: 'radar-signals',
  ownership: 'ownership',
  dataStatus: 'data-status',
} as const;

export const VALUATION_CARD_TITLE = 'Is it cheap?';
export const CRYPTO_PRICE_CARD_TITLE = 'Price & your target';

export const DOSSIER_SECTION_CHIPS = {
  checks: {id: DOSSIER_SECTION_ID.checks, label: 'Checks'},
  chart: {id: DOSSIER_SECTION_ID.chart, label: 'Chart'},
  returns: {id: DOSSIER_SECTION_ID.returns, label: 'Returns'},
  valuation: {id: DOSSIER_SECTION_ID.valuation, label: 'Valuation'},
  cryptoPrice: {id: DOSSIER_SECTION_ID.valuation, label: CRYPTO_PRICE_CARD_TITLE},
  analysts: {id: DOSSIER_SECTION_ID.analysts, label: 'Analysts'},
  thesis: {id: DOSSIER_SECTION_ID.thesis, label: 'Plan'},
  earnings: {id: DOSSIER_SECTION_ID.earnings, label: 'Earnings'},
  news: {id: DOSSIER_SECTION_ID.news, label: 'News'},
  signals: {id: DOSSIER_SECTION_ID.signals, label: 'Signals'},
  dataStatus: {id: DOSSIER_SECTION_ID.dataStatus, label: 'Data'},
} as const satisfies Record<string, DossierSectionChip>;
