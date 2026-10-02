import {ApplicationInitStatus} from '@angular/core';
import {TestBed} from '@angular/core/testing';

import {provideAppTheme} from './theme.provider';

const THEME_STORAGE_KEY = 'cmn-theme';

function stubSystemDark(matches: boolean): void {
  vi.stubGlobal(
    'matchMedia',
    vi.fn().mockReturnValue({matches, addEventListener: vi.fn(), removeEventListener: vi.fn()})
  );
}

async function boot(): Promise<void> {
  TestBed.configureTestingModule({providers: [provideAppTheme()]});
  await TestBed.inject(ApplicationInitStatus).donePromise;
}

describe('provideAppTheme', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('applies the stored dark theme at startup', async () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark');
    stubSystemDark(false);

    await boot();

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('keeps an explicit stored light choice even when the OS prefers dark', async () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    stubSystemDark(true);

    await boot();

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('falls back to the OS dark preference when nothing is stored', async () => {
    stubSystemDark(true);

    await boot();

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('stays light when nothing is stored and the OS prefers light', async () => {
    stubSystemDark(false);

    await boot();

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });
});
