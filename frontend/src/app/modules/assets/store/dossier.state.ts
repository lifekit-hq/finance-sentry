import {
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';

export interface DossierState {
  dossier: Nullable<AssetDossierDto>;
  ledgerRead: Nullable<AssetLedgerReadDto>;
  isThesisExpanded: boolean;
  quote: Nullable<DossierQuoteDto>;
}

export const initialDossierState: DossierState = {
  dossier: null,
  ledgerRead: null,
  isThesisExpanded: false,
  quote: null,
};
