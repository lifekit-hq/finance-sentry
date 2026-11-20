import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type RiskPicture} from '../../models/risk-picture/risk-picture.model';
import {type RiskPictureState} from './risk-picture.state';

export function riskPictureMethods(store: WritableStateSource<RiskPictureState>) {
  return {
    setPicture(picture: RiskPicture): void {
      patchState(store, {picture});
    },
  };
}
