import {type AssetDossierDto, type AssetLedgerReadDto} from '../models/dossier/dossier.model';

export interface DossierState {
  dossier: Nullable<AssetDossierDto>;
  dossierStatus: AsyncStatus;
  dossierErrorCode: Nullable<string>;
  ledgerRead: Nullable<AssetLedgerReadDto>;
  ledgerReadStatus: AsyncStatus;
  ledgerReadErrorCode: Nullable<string>;
  isThesisExpanded: boolean;
}

export const initialDossierState: DossierState = {
  dossier: null,
  dossierStatus: 'idle',
  dossierErrorCode: null,
  ledgerRead: null,
  ledgerReadStatus: 'idle',
  ledgerReadErrorCode: null,
  isThesisExpanded: false,
};
