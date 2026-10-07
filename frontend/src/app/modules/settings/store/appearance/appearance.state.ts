import {DEFAULT_INTENSITY} from '@lifekit-hq/tokens/engine';

export interface AppearanceState {
  /** The user's colour on this device; `null` while the app's own palette applies. */
  seed: Nullable<string>;
  intensity: number;
}

export const initialAppearanceState: AppearanceState = {
  seed: null,
  intensity: DEFAULT_INTENSITY,
};
