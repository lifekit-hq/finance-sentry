/** localStorage key for whether this device shows the exchange-rate insight on Subscriptions. */
export const FX_IMPACT_ENABLED_STORAGE_KEY = 'fns.subscriptions.fxImpactEnabled';

/** Shown on a first visit: the insight is on until someone switches it off. */
export const FX_IMPACT_DEFAULT_ENABLED = true;

/** A saving below half a cent in magnitude reads as no change. */
export const FX_IMPACT_FLAT_THRESHOLD = 0.005;
