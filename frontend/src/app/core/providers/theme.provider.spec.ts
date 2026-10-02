import {ApplicationInitStatus} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ThemeService} from '@lifekit-hq/ui';
import {firstValueFrom} from 'rxjs';

import {provideAppTheme} from './theme.provider';

const THEME_STORAGE_KEY = 'cmn-theme';

type SchemeListener = (event: {matches: boolean}) => void;

function stubSystemDark(matches: boolean): {emit: SchemeListener} {
  const listeners: SchemeListener[] = [];
  vi.stubGlobal(
    'matchMedia',
    vi.fn().mockReturnValue({
      matches,
      addEventListener: (_type: string, listener: SchemeListener) => listeners.push(listener),
      removeEventListener: vi.fn(),
    })
  );
  return {emit: event => listeners.forEach(listener => listener(event))};
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

  it('does not persist the OS preference as an explicit choice', async () => {
    stubSystemDark(true);

    await boot();

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
  });

  it('syncs the service state with the OS theme and persists only on an explicit toggle', async () => {
    stubSystemDark(true);
    await boot();
    const theme = TestBed.inject(ThemeService);

    expect(await firstValueFrom(theme.activeTheme$)).toBe('dark');

    theme.toggle();

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
  });

  it('follows OS preference changes while nothing is stored', async () => {
    const system = stubSystemDark(true);
    await boot();

    system.emit({matches: false});
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');

    system.emit({matches: true});
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
  });

  it('ignores OS preference changes once the user has chosen a theme', async () => {
    const system = stubSystemDark(true);
    await boot();
    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    document.documentElement.setAttribute('data-theme', 'light');

    system.emit({matches: true});

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('stays light when nothing is stored and the OS prefers light', async () => {
    stubSystemDark(false);

    await boot();

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });
});
