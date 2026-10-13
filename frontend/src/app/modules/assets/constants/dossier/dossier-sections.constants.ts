import {type DossierSectionChip} from '../../models/dossier/dossier.model';

/** Anchor ids of the dossier sections; the sticky chip bar and alert fragments scroll to them. */
export const DOSSIER_SECTION_ID = {
  thesis: 'thesis',
  valuation: 'valuation',
  analysts: 'analyst-coverage',
  earnings: 'next-earnings',
  news: 'recent-news',
  signals: 'radar-signals',
} as const;

export const DOSSIER_SECTION_CHIPS = {
  thesis: {id: DOSSIER_SECTION_ID.thesis, label: 'Thesis'},
  valuation: {id: DOSSIER_SECTION_ID.valuation, label: 'Valuation'},
  analysts: {id: DOSSIER_SECTION_ID.analysts, label: 'Analysts'},
  earnings: {id: DOSSIER_SECTION_ID.earnings, label: 'Earnings'},
  news: {id: DOSSIER_SECTION_ID.news, label: 'News'},
  signals: {id: DOSSIER_SECTION_ID.signals, label: 'Signals'},
} as const satisfies Record<string, DossierSectionChip>;
