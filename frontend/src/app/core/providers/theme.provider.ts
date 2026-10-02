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

function applySystemTheme(dark: boolean): void {
  document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
}

/**
 * Instantiates ThemeService at app start so the stored theme applies on every route,
 * guest pages included. With no stored choice the OS preference is followed live and
 * never persisted; only an explicit ThemeService.setTheme/toggle writes the key.
 */
export function provideAppTheme(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      inject(ThemeService);
      const systemDark = window.matchMedia?.(DARK_SCHEME_QUERY);
      if (!systemDark || readStoredTheme() !== null) {
        return;
      }
      applySystemTheme(systemDark.matches);
      systemDark.addEventListener('change', event => {
        if (readStoredTheme() === null) {
          applySystemTheme(event.matches);
        }
      });
    }),
  ]);
}
