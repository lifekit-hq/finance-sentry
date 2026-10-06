import {type FireProjection} from '../../models/fire/fire.model';

export interface FireState {
  projection: Nullable<FireProjection>;
  status: AsyncStatus;
  errorCode: Nullable<string>;
}

export const initialFireState: FireState = {
  projection: null,
  status: 'idle',
  errorCode: null,
};
