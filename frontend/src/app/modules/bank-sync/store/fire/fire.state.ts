import {type Nullable} from '@lifekit-hq/ui';

import {type FireProjection} from '../../models/fire/fire.model';

export interface FireState {
  projection: Nullable<FireProjection>;
}

export const initialFireState: FireState = {
  projection: null,
};
