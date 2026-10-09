import {type InzhurConnectionStatus} from '../../models/inzhur/inzhur.model';

export type InzhurLoadStatus = 'loading' | 'loaded' | 'error';

export interface InzhurConnectState {
  connection: Nullable<InzhurConnectionStatus>;
  statusLoad: InzhurLoadStatus;
}

export const initialInzhurConnectState: InzhurConnectState = {
  connection: null,
  statusLoad: 'loading',
};
