import {type PushDevice} from '../../models/push/push.model';

export type PushAction = 'idle' | 'enabling' | 'saving' | 'removing';

export interface PushState {
  status: AsyncStatus;
  errorCode: Nullable<string>;
  available: boolean;
  publicKey: Nullable<string>;
  pushEnabled: boolean;
  devices: PushDevice[];
  /** The id of this browser's registered subscription, when it has one. */
  thisDeviceId: Nullable<string>;
  action: PushAction;
  actionErrorCode: Nullable<string>;
  removingId: Nullable<string>;
}

export const initialPushState: PushState = {
  status: 'idle',
  errorCode: null,
  available: false,
  publicKey: null,
  pushEnabled: false,
  devices: [],
  thisDeviceId: null,
  action: 'idle',
  actionErrorCode: null,
  removingId: null,
};
