import {type Nullable} from '@lifekit-hq/ui';

import {type RiskPicture} from '../../models/risk-picture/risk-picture.model';

export interface RiskPictureState {
  picture: Nullable<RiskPicture>;
}

export const initialRiskPictureState: RiskPictureState = {picture: null};
