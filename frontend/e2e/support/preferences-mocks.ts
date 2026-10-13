import {type Page} from '@playwright/test';

const API = '**/api/v1';

export interface MockAppearance {
  theme: 'system' | 'light' | 'dark';
  seed: string | null;
  intensity: number | null;
}

/**
 * Stands in for `GET/PATCH /api/v1/me/preferences`: PATCH writes the sent appearance as a whole (an
 * omitted seed clears it, as the standard defines), so a reload after a change reads back what was
 * saved, as the real endpoint does.
 */
export async function mockPreferences(
  page: Page,
  initial: MockAppearance = {theme: 'system', seed: null, intensity: null}
): Promise<{patches: unknown[]}> {
  let appearance = initial;
  const patches: unknown[] = [];
  await page.route(`${API}/me/preferences`, route => {
    if (route.request().method() === 'PATCH') {
      const body = route.request().postDataJSON() as {appearance?: Partial<MockAppearance>};
      patches.push(body);
      if (body.appearance) {
        appearance = {theme: 'system', seed: null, intensity: null, ...body.appearance};
      }
    }
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({appearance}),
    });
  });
  return {patches};
}
