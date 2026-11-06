import {type TrueLayerProvider} from '../../models/truelayer/truelayer.model';

export interface TrueLayerPickerState {
  providers: TrueLayerProvider[];
}

export const initialTrueLayerPickerState: TrueLayerPickerState = {
  providers: [],
};
