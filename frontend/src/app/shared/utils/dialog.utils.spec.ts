import {afterEach, describe, expect, it, vi} from 'vitest';

import {DialogUtils} from './dialog.utils';

describe('DialogUtils.phoneTitle', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function stubViewport(matchesMd: boolean): void {
    vi.stubGlobal('matchMedia', (query: string) => ({matches: matchesMd, media: query}));
  }

  it('returns the title below the md breakpoint', () => {
    stubViewport(false);
    expect(DialogUtils.phoneTitle('Remove PLTR?')).toBe('Remove PLTR?');
  });

  it('returns undefined at md and up so the dialog has no second heading', () => {
    stubViewport(true);
    expect(DialogUtils.phoneTitle('Remove PLTR?')).toBeUndefined();
  });
});
