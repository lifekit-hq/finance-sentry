import {expect, type Page} from '@playwright/test';

import {API} from '../dossier-mocks';
import {json, type VisualScreen} from '../visual-baseline';

const UNAUTHORIZED = 401;

/**
 * The screens outside the shell are opened with no session: the shared shell mocks answer as a
 * signed-in person, so each of these puts `auth/me` and `auth/refresh` back to 401 first.
 */
async function signedOut(page: Page): Promise<void> {
  const unauthorized = (route: {fulfill: (o: {status: number}) => Promise<void>}): Promise<void> =>
    route.fulfill({status: UNAUTHORIZED});
  await page.route(`${API}/auth/me`, unauthorized);
  await page.route(`${API}/auth/refresh`, unauthorized);
}

export const LOGIN_SCREEN: VisualScreen = {
  name: 'login',
  title: 'Sign in',
  path: '/login',
  mocks: async page => {
    await signedOut(page);
    // A sign-out message holds the automatic forward to the identity provider, so the page rests on
    // its Sign in button instead of leaving. The route is shared with the screen gates, so the
    // query is added before the app starts rather than put on the route's path.
    await page.addInitScript(() => {
      if (window.location.pathname === '/login') {
        window.history.replaceState(null, '', '/login?info=signed_out');
      }
    });
    await page.route(`${API}/auth/methods`, route => route.fulfill(json({oidc: true})));
    await page.route(`${API}/auth/oidc/start**`, route => route.fulfill({status: 204}));
  },
  ready: async page => {
    await expect(page.getByText('You have been signed out.')).toBeVisible();
    await expect(page.getByRole('button', {name: 'Sign in'})).toBeVisible();
  },
};

export const ACCESS_DENIED_SCREEN: VisualScreen = {
  name: 'access-denied',
  title: 'No access',
  path: '/access-denied',
  mocks: signedOut,
  ready: async page => {
    await expect(page.getByTestId('access-denied-message')).toBeVisible();
  },
};

export const MCP_CONNECT_SCREEN: VisualScreen = {
  name: 'mcp-connect',
  title: 'Connect MCP',
  // No redirect parameters: the page stops on its message instead of leaving for the API.
  path: '/mcp/connect',
  mocks: signedOut,
  ready: async page => {
    await expect(page.getByText('Missing MCP authorization parameters.')).toBeVisible();
  },
};

export const PRIVACY_SCREEN: VisualScreen = {
  name: 'privacy',
  title: 'Privacy notice',
  path: '/privacy',
  mocks: signedOut,
  ready: async page => {
    await expect(page.getByTestId('privacy-notice')).toBeVisible();
  },
};

export const TERMS_SCREEN: VisualScreen = {
  name: 'terms',
  title: 'Terms of use',
  path: '/terms',
  mocks: signedOut,
  ready: async page => {
    await expect(page.getByTestId('not-advice')).toBeVisible();
  },
};
