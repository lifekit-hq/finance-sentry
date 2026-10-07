/** What `lk-theme-picker-change` carries: the chosen colour, or `null` for the app's own. */
export interface ThemeChoice {
  seed: Nullable<string>;
  intensity: number;
}
