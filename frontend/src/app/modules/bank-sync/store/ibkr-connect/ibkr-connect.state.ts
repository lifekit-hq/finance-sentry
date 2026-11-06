import {type Nullable} from '@lifekit-hq/ui';

import {type IbkrFlexPreview} from '../../models/ibkr/ibkr.model';

export type IbkrConnectPath = 'flex' | 'oauth';

export const DEFAULT_VALIDATION_ERROR =
  "Couldn't check your Flex query. Check the token and query ID and try again.";

export interface IbkrConnectState {
  /** Which connect path the user last submitted — decides where a connect error is shown. */
  path: IbkrConnectPath;
  preview: Nullable<IbkrFlexPreview>;
}

export const initialIbkrConnectState: IbkrConnectState = {
  path: 'flex',
  preview: null,
};
