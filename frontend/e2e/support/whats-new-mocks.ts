import {type Page} from '@playwright/test';

import {APP_VERSION} from '../../src/app/shared/constants/version/version.constants';

const SEEN_KEY = 'fns.whatsNew.lastSeen';
/** Older than any release, so the running version is always newer than what this device has seen. */
const STALE_SEEN_VERSION = '0.0.1';

interface FixtureNote {
  text: string;
  owner: boolean;
}

/**
 * A What's new feed whose newest entry is the running app version, so the unread dot does not
 * depend on which release the build is (a release bump moves `APP_VERSION` and would otherwise
 * leave a hard-coded entry behind).
 */
export function whatsNewFixture(notes: FixtureNote[]): {versions: object[]} {
  return {
    versions: [
      {version: APP_VERSION, date: '2026-10-05', notes, changes: ['Group transactions by day']},
      {version: STALE_SEEN_VERSION, date: '2026-01-01', notes: [], changes: ['Older change']},
    ],
  };
}

/** Marks an older version as last seen and serves the feed, so the running version reads as unread. */
export async function seedUnreadWhatsNew(page: Page, notes: FixtureNote[]): Promise<void> {
  await page.addInitScript(
    ([key, version]) => localStorage.setItem(key, version),
    [SEEN_KEY, STALE_SEEN_VERSION]
  );
  await page.route('**/whats-new.json*', route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(whatsNewFixture(notes))})
  );
}
