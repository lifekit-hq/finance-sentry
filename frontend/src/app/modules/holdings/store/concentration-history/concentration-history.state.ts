import {type Nullable} from '@lifekit-hq/ui';

import {
  type ConcentrationHistory,
  type ConcentrationMeasure,
} from '../../models/risk-picture/risk-picture.model';

export interface ConcentrationHistoryState {
  history: Nullable<ConcentrationHistory>;
  measure: ConcentrationMeasure;
}

export const initialConcentrationHistoryState: ConcentrationHistoryState = {
  history: null,
  measure: 'top3',
};
