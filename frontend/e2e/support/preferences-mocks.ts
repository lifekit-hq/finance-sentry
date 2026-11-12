import {type Page} from '@playwright/test';

const API = '**/api/v1';

export interface MockAppearance {
  theme: 'system' | 'light' | 'dark';
  seed: string | null;
  intensity: number | null;
}

export type MockVerdictDisplay = 'simple' | 'detailed';

/**
 * Stands in for `GET/PATCH /api/v1/me/preferences`: PATCH writes the sent appearance as a whole (an
 * omitted seed clears it, as the standard defines) and the sent verdict display on its own, so a reload
 * after a change reads back what was saved, as the real endpoint does.
 */
export async function mockPreferences(
  page: Page,
  initial: MockAppearance = {theme: 'system', seed: null, intensity: null},
  initialVerdictDisplay: MockVerdictDisplay = 'simple'
): Promise<{patches: unknown[]}> {
  let appearance = initial;
  let verdictDisplay = initialVerdictDisplay;
  const patches: unknown[] = [];
  await page.route(`${API}/me/preferences`, route => {
    if (route.request().method() === 'PATCH') {
      const body = route.request().postDataJSON() as {
        appearance?: Partial<MockAppearance>;
        verdictDisplay?: MockVerdictDisplay;
      };
      patches.push(body);
      if (body.appearance) {
        appearance = {theme: 'system', seed: null, intensity: null, ...body.appearance};
      }
      if (body.verdictDisplay) {
        verdictDisplay = body.verdictDisplay;
      }
    }
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({appearance, verdictDisplay}),
    });
  });
  return {patches};
}
