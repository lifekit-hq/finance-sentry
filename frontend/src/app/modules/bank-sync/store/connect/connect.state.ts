import {type Nullable} from '@lifekit-hq/ui';

import {type Provider} from '../../../../shared/models/provider/provider.model';
import {type InstitutionType} from '../../../../shared/models/provider/provider.model';
import {type ModalStep} from '../../models/connect/connect.model';

export interface ConnectState {
  selectedProvider: Provider;
  statusMessage: Nullable<string>;
  modalStep: ModalStep;
  institutionType: Nullable<InstitutionType>;
  importPending: boolean;
}

export const initialConnectState: ConnectState = {
  selectedProvider: 'truelayer',
  statusMessage: null,
  modalStep: 'closed',
  institutionType: null,
  importPending: false,
};
