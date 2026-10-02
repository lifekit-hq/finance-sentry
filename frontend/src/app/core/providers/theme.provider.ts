import {
  type EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import {ThemeService} from '@lifekit-hq/ui';

import {type ThemePreference} from '../../modules/settings/models/settings/settings.model';

const THEME_STORAGE_KEY = 'cmn-theme';
const DARK_SCHEME_QUERY = '(prefers-color-scheme: dark)';

function readStoredTheme(): string | null {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY);
  } catch {
    return null;
  }
}

function clearStoredTheme(): void {
  try {
    localStorage.removeItem(THEME_STORAGE_KEY);
  } catch {
    // storage unavailable: nothing was persisted
  }
}

function applySystemTheme(theme: ThemeService, dark: boolean): void {
  theme.setTheme(dark ? 'dark' : 'light');
  clearStoredTheme();
}

/** Applies a saved profile preference live: light/dark persist as an explicit choice, system follows the OS. */
export function applyThemePreference(theme: ThemeService, preference: ThemePreference): void {
  if (preference !== 'system') {
    theme.setTheme(preference);
    return;
  }
  const systemDark = window.matchMedia?.(DARK_SCHEME_QUERY);
  if (systemDark) {
    applySystemTheme(theme, systemDark.matches);
  }
}

/**
 * Instantiates ThemeService at app start so the stored theme applies on every route,
 * guest pages included. With no stored choice the OS preference is followed live and
 * never persisted (the service state is synced, then its key cleared); only an explicit
 * ThemeService.setTheme/toggle writes the key.
 */
export function provideAppTheme(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      try {
        const theme = inject(ThemeService);
        const systemDark = window.matchMedia?.(DARK_SCHEME_QUERY);
        if (!systemDark || readStoredTheme() !== null) {
          return;
        }
        applySystemTheme(theme, systemDark.matches);
        systemDark.addEventListener('change', event => {
          if (readStoredTheme() === null) {
            applySystemTheme(theme, event.matches);
          }
        });
      } catch {
        // blocked storage: keep the theme the pre-paint script applied
      }
    }),
  ]);
}
