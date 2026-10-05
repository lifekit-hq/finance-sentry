import {type Provider} from '../../../../shared/models/provider/provider.model';
import {type InstitutionType} from '../../../../shared/models/provider/provider.model';
import {type ModalStep} from '../../models/connect/connect.model';

export type ConnectStatus =
  'idle' | 'initializing' | 'ready' | 'syncing' | 'polling' | 'error' | 'success';

export interface ConnectState {
  selectedProvider: Provider;
  status: ConnectStatus;
  errorCode: Nullable<string>;
  statusMessage: Nullable<string>;
  modalStep: ModalStep;
  institutionType: Nullable<InstitutionType>;
  importPending: boolean;
}

export const initialConnectState: ConnectState = {
  selectedProvider: 'truelayer',
  status: 'idle',
  errorCode: null,
  statusMessage: null,
  modalStep: 'closed',
  institutionType: null,
  importPending: false,
};
