import {type InzhurConnectionStatus} from '../../models/inzhur/inzhur.model';

export type InzhurLoadStatus = 'loading' | 'loaded' | 'error';

export type InzhurFormStep = 'credentials' | 'code';

export interface InzhurConnectState {
  connection: Nullable<InzhurConnectionStatus>;
  statusLoad: InzhurLoadStatus;
  step: InzhurFormStep;
  /** The owner chose to type a phone and password instead of reusing the saved ones. */
  editingCredentials: boolean;
  starting: boolean;
  codeExpiresAt: Nullable<string>;
  /** Why the sign-in (the step that sends the SMS) failed; the code step's errors live in ConnectStore. */
  errorCode: Nullable<string>;
}

export const initialInzhurConnectState: InzhurConnectState = {
  connection: null,
  statusLoad: 'loading',
  step: 'credentials',
  editingCredentials: false,
  starting: false,
  codeExpiresAt: null,
  errorCode: null,
};
