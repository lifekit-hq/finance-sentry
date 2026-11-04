import {type Nullable} from '@lifekit-hq/ui';

import {type InzhurConnectionStatus} from '../../models/inzhur/inzhur.model';

export interface InzhurConnectState {
  connection: Nullable<InzhurConnectionStatus>;
}

export const initialInzhurConnectState: InzhurConnectState = {
  connection: null,
};
