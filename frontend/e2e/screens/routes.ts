import {expect, type Page} from '@playwright/test';
import type {PageKind} from '@lifekit-hq/ui';

import {mockDashboard} from '../support/dashboard-mocks';
import {mockApis} from '../support/dossier-mocks';
import {PLACEHOLDER_IMAGE, type VisualScreen} from '../support/visual-baseline';
import {ACCOUNTS_SCREEN} from '../support/screens/accounts.screen';
import {ALERTS_SCREEN} from '../support/screens/alerts.screen';
import {BUDGETS_SCREEN} from '../support/screens/budgets.screen';
import {DECISION_DETAIL_SCREEN, DECISIONS_SCREEN} from '../support/screens/decisions.screen';
import {EVENTS_SCREEN} from '../support/screens/events.screen';
import {FLOW_BREAKDOWN_SCREEN} from '../support/screens/flow-breakdown.screen';
import {LEDGER_SCREEN} from '../support/screens/ledger.screen';
import {MORE_SCREEN} from '../support/screens/more.screen';
import {PEOPLE_SCREEN} from '../support/screens/people.screen';
import {SETTINGS_SCREEN} from '../support/screens/settings.screen';
import {SUBSCRIPTIONS_SCREEN} from '../support/screens/subscriptions.screen';
import {TRANSACTIONS_SCREEN} from '../support/screens/transactions.screen';
import {WATCHLIST_SCREEN} from '../support/screens/watchlist.screen';

/**
 * How a route answers the screen gates:
 * - `report`: the gates run and print what they find, and the test passes. The state every route
 *   starts in, so a page area can be adopted one at a time.
 * - `enforce`: a finding fails the test. A route moves here in the pull request that adopts its
 *   page template (`cmn-page`) and leaves nothing to report.
 * - `pending`: no fixture data yet, so the gates cannot open it. Listed so the route list names every
 *   routed screen; wiring its fixtures moves it to `report`.
 */
export type RouteMode = 'report' | 'enforce' | 'pending';

interface RouteBase {
  /** Names the screen in test titles, baseline files and exceptions; unique and kebab-case. */
  name: string;
  /** The route the router serves (`app.routes.ts`), with a concrete value for any parameter. */
  path: string;
  /**
   * The page template (`cmn-page` kind) the screen is meant to be built on, or `null` for a surface
   * that is not a page (a full-bleed canvas), with the reason in `note`.
   */
  kind: PageKind | null;
  /** One line: why the kind, or why the route is not wired. */
  note: string;
}

/** A routed screen the gates open: its fixture data, and the element that proves it rendered. */
export interface WiredRoute extends RouteBase {
  mode: 'report' | 'enforce';
  fixture: Pick<VisualScreen, 'mocks' | 'ready'>;
}

export interface PendingRoute extends RouteBase {
  mode: 'pending';
}

export type ScreenRoute = WiredRoute | PendingRoute;

const DOSSIER_LOGO_HOSTS = ['https://financialmodelingprep.com/**', 'https://assets.coincap.io/**'];

// The shell's chrome (sidebar, title bar) is on every route; the page is what the gates measure.
function fixtureOf(screen: VisualScreen): WiredRoute['fixture'] {
  return {mocks: screen.mocks, ready: screen.ready};
}

async function mockDossier(page: Page): Promise<void> {
  for (const pattern of DOSSIER_LOGO_HOSTS) {
    await page.route(pattern, route =>
      route.fulfill({status: 200, contentType: 'image/png', body: PLACEHOLDER_IMAGE})
    );
  }
  await mockApis(page);
}

/**
 * Every routed screen of the app (`app.routes.ts`, `bank-sync.routes.ts`), with the page kind it is
 * meant to be built on. The screen check, the baselines and the route scan all read this list, so a
 * route added to the router is added here once. `routes.spec.ts` fails when the two drift apart.
 */
export const SCREEN_ROUTES: readonly ScreenRoute[] = [
  {
    name: 'dashboard',
    path: '/dashboard',
    kind: 'overview',
    note: 'Net worth hero, window tiles and the where-it-is rail.',
    mode: 'report',
    fixture: {
      mocks: mockDashboard,
      ready: async page => {
        await expect(page.getByTestId('net-worth-value')).toContainText('$50,000.00');
        await expect(page.getByTestId('net-worth-allocation')).toBeVisible();
      },
    },
  },
  {
    name: 'flow-breakdown',
    path: FLOW_BREAKDOWN_SCREEN.path,
    kind: 'overview',
    note: 'One month or window broken into its flows; reached from the dashboard. Needs the full frame width: the stat cards wrap amounts in the detail column.',
    mode: 'report',
    fixture: fixtureOf(FLOW_BREAKDOWN_SCREEN),
  },
  {
    name: 'accounts',
    path: ACCOUNTS_SCREEN.path,
    kind: 'list',
    note: 'Connected institutions and their accounts.',
    mode: 'report',
    fixture: fixtureOf(ACCOUNTS_SCREEN),
  },
  {
    name: 'transactions',
    path: TRANSACTIONS_SCREEN.path,
    kind: 'list',
    note: 'The ledger of transactions with its filters.',
    mode: 'report',
    fixture: fixtureOf(TRANSACTIONS_SCREEN),
  },
  {
    name: 'budgets',
    path: BUDGETS_SCREEN.path,
    kind: 'overview',
    note: 'A month summary over a responsive card grid of category estimates.',
    mode: 'enforce',
    fixture: fixtureOf(BUDGETS_SCREEN),
  },
  {
    name: 'subscriptions',
    path: SUBSCRIPTIONS_SCREEN.path,
    kind: 'list',
    note: 'Detected recurring charges.',
    mode: 'enforce',
    fixture: fixtureOf(SUBSCRIPTIONS_SCREEN),
  },
  {
    name: 'watchlist',
    path: WATCHLIST_SCREEN.path,
    kind: 'list',
    note: 'Watched assets.',
    mode: 'enforce',
    fixture: fixtureOf(WATCHLIST_SCREEN),
  },
  {
    name: 'alerts',
    path: ALERTS_SCREEN.path,
    kind: 'list',
    note: 'The alert feed.',
    mode: 'enforce',
    fixture: fixtureOf(ALERTS_SCREEN),
  },
  {
    name: 'events',
    path: EVENTS_SCREEN.path,
    kind: 'list',
    note: 'Upcoming and fired events.',
    mode: 'enforce',
    fixture: fixtureOf(EVENTS_SCREEN),
  },
  {
    name: 'decisions',
    path: DECISIONS_SCREEN.path,
    kind: 'list',
    note: "The Ledger's decision queue.",
    mode: 'enforce',
    fixture: fixtureOf(DECISIONS_SCREEN),
  },
  {
    name: 'decision-detail',
    path: DECISION_DETAIL_SCREEN.path,
    kind: 'detail',
    note: 'One Ledger proposal, pushed from the queue, with Reject and Confirm pinned.',
    mode: 'enforce',
    fixture: fixtureOf(DECISION_DETAIL_SCREEN),
  },
  {
    name: 'ledger-chat',
    path: LEDGER_SCREEN.path,
    kind: null,
    note: 'A chat canvas that fills the shell; it is not a page with a title and columns.',
    mode: 'report',
    fixture: fixtureOf(LEDGER_SCREEN),
  },
  {
    name: 'asset-dossier',
    path: '/assets/AAPL',
    kind: 'overview',
    note: 'One holding: price, checks and the Ledger read. Needs the full frame width: the chip and tab rows clip in the detail column.',
    mode: 'report',
    fixture: {
      mocks: mockDossier,
      ready: async page => {
        await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();
      },
    },
  },
  {
    name: 'settings',
    path: SETTINGS_SCREEN.path,
    kind: 'settings',
    note: 'Preferences, notifications and the account.',
    mode: 'enforce',
    fixture: fixtureOf(SETTINGS_SCREEN),
  },
  {
    name: 'people',
    path: PEOPLE_SCREEN.path,
    kind: 'list',
    note: 'Who has access; Owner only.',
    mode: 'enforce',
    fixture: fixtureOf(PEOPLE_SCREEN),
  },
  {
    name: 'more',
    path: MORE_SCREEN.path,
    kind: 'list',
    note: 'The phone shell’s overflow menu.',
    mode: 'enforce',
    fixture: fixtureOf(MORE_SCREEN),
  },
  {
    name: 'construction',
    path: '/construction',
    kind: 'list',
    note: 'The universe screen; needs fixtures for the AI-gated portfolio endpoints.',
    mode: 'pending',
  },
  {
    name: 'login',
    path: '/login',
    kind: 'form',
    note: 'Signed-out screen outside the shell; needs a signed-out fixture.',
    mode: 'pending',
  },
  {
    name: 'access-denied',
    path: '/access-denied',
    kind: 'detail',
    note: 'Signed-out notice outside the shell; needs a signed-out fixture.',
    mode: 'pending',
  },
  {
    name: 'mcp-connect',
    path: '/mcp/connect',
    kind: 'form',
    note: 'Consent step of the MCP connection; needs a pending-authorization fixture.',
    mode: 'pending',
  },
  {
    name: 'privacy',
    path: '/privacy',
    kind: 'detail',
    note: 'A reading page, public.',
    mode: 'pending',
  },
  {
    name: 'terms',
    path: '/terms',
    kind: 'detail',
    note: 'A reading page, public.',
    mode: 'pending',
  },
];

export function wiredRoutes(): WiredRoute[] {
  return SCREEN_ROUTES.filter((route): route is WiredRoute => route.mode !== 'pending');
}
