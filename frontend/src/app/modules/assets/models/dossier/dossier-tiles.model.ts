import {type DossierMoveLinkDto} from './dossier.model';

/** One notable move as the grid shows it: its tile, its magnitude and what the news and signals around it add up to. */
export interface DossierMoveView {
  tileId: string;
  date: string;
  returnPct: number;
  direction: 'up' | 'down';
  newsCount: number;
  signalCount: number;
  links: DossierMoveLinkDto[];
}
