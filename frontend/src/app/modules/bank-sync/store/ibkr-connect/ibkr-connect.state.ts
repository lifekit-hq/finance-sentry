import {type IbkrFlexPreview} from '../../models/ibkr/ibkr.model';

export type IbkrConnectPath = 'flex' | 'oauth';

export type IbkrValidationStatus = 'idle' | 'validating' | 'validated' | 'error';

export interface IbkrConnectState {
  /** Which connect path the user last submitted — decides where a connect error is shown. */
  path: IbkrConnectPath;
  validationStatus: IbkrValidationStatus;
  preview: Nullable<IbkrFlexPreview>;
  errorCode: Nullable<string>;
}

export const initialIbkrConnectState: IbkrConnectState = {
  path: 'flex',
  validationStatus: 'idle',
  preview: null,
  errorCode: null,
};
