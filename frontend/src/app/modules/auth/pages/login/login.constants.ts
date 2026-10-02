// Pin the Google button to the app language instead of the browser locale.
export const GOOGLE_BUTTON_LOCALE = 'en';

// Google Identity Services accepts a numeric button width of 200-400px.
export const GOOGLE_BUTTON_MIN_WIDTH = 200;
export const GOOGLE_BUTTON_MAX_WIDTH = 400;

// Library defaults for every button option except width, which is measured at runtime.
export const GOOGLE_BUTTON_BASE_CONFIG = {
  type: 'standard',
  shape: 'rectangular',
  theme: 'outline',
  text: 'continue_with',
  size: 'large',
} as const;
