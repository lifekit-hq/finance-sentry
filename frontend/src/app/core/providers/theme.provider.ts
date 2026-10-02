import {
  type EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import {ThemeService} from '@lifekit-hq/ui';

const THEME_STORAGE_KEY = 'cmn-theme';
const DARK_SCHEME_QUERY = '(prefers-color-scheme: dark)';

function readStoredTheme(): string | null {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY);
  } catch {
    return null;
  }
}

/**
 * Instantiates ThemeService at app start so the stored theme (or, with no stored
 * choice, the OS preference) applies on every route, guest pages included.
 */
export function provideAppTheme(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      const theme = inject(ThemeService);
      const hasStoredChoice = readStoredTheme() !== null;
      if (!hasStoredChoice && window.matchMedia?.(DARK_SCHEME_QUERY).matches) {
        theme.setTheme('dark');
      }
    }),
  ]);
}
