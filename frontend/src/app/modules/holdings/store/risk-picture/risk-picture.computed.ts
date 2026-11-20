import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type RiskFigureRow, type RiskPicture} from '../../models/risk-picture/risk-picture.model';
import {RiskPictureUtils} from '../../utils/risk-picture.utils';

interface StateSignals {
  picture: Signal<Nullable<RiskPicture>>;
}

export function riskPictureComputed(store: StateSignals) {
  return {
    rows: computed<RiskFigureRow[]>(() => {
      const picture = store.picture();
      return picture ? RiskPictureUtils.rows(picture) : [];
    }),
  };
}
