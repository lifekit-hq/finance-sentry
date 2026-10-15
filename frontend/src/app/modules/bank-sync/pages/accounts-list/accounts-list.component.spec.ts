import {BreakpointObserver, type BreakpointState} from '@angular/cdk/layout';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, provideRouter, Router} from '@angular/router';
import {CmnDialogService} from '@lifekit-hq/ui';
import {of} from 'rxjs';

import {type Headline} from '../../../../shared/models/headline/headline.model';
import {
  type AccountBalanceItem,
  type Institution,
} from '../../../../shared/models/wealth/wealth.model';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {AccountsListComponent} from './accounts-list.component';

const baseInstitution: Institution = {
  institutionId: 'inst-1',
  provider: 'truelayer',
  name: 'Test Bank',
  category: 'banking',
  totalInBaseCurrency: 100,
  syncStatus: 'failed',
  lastSyncTimestamp: null,
  lastSuccessfulSyncTimestamp: null,
  accounts: [],
};

function setupFixture(
  isPhone: boolean,
  overrides: Partial<Institution> = {},
  extraProviders: unknown[] = [],
  state: {
    isLoading?: boolean;
    isEmpty?: boolean;
    errorMessage?: string;
    hasData?: boolean;
    load?: () => void;
    totalNetWorth?: number;
    headline?: Headline;
    collapsed?: string[];
    setInstitutionOpen?: (key: string, isOpen: boolean) => void;
  } = {}
) {
  const institution: Institution = {...baseInstitution, ...overrides};
  const store = {
    isLoading: signal(state.isLoading ?? false),
    isEmpty: signal(state.isEmpty ?? false),
    errorMessage: signal(state.errorMessage ?? ''),
    summary: signal(state.hasData ? {} : null),
    lastSyncedAt: signal<number | null>(state.hasData ? Date.now() : null),
    load: state.load ?? vi.fn(),
    collapsedInstitutions: signal(state.collapsed ?? []),
    setInstitutionOpen: state.setInstitutionOpen ?? vi.fn(),
    totalNetWorth: signal(state.totalNetWorth ?? 0),
    netWorthHeadline: signal(state.headline ?? null),
    isNetWorthUnavailable: signal(state.headline?.quality === 'UNAVAILABLE'),
    netWorthBreakdown: signal([]),
    baseCurrency: signal('USD'),
    categorySections: signal([
      {
        category: institution.category,
        title: 'Banks',
        institutionNoun: 'bank',
        rowNoun: institution.category === 'banking' ? 'account' : 'position',
        summary: {
          category: institution.category,
          totalInBaseCurrency: 100,
          institutionCount: 1,
          institutions: [institution],
        },
      },
    ]),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: AccountsStore, useValue: store},
      {provide: ConnectStore, useValue: {}},
      {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: new Map()}}},
      {
        provide: BreakpointObserver,
        useValue: {observe: () => of({matches: isPhone, breakpoints: {}} as BreakpointState)},
      },
      ...(extraProviders as never[]),
    ],
  });
  const fixture = TestBed.createComponent(AccountsListComponent);
  fixture.detectChanges();
  return fixture;
}

function setup(isPhone: boolean, overrides: Partial<Institution> = {}): HTMLElement {
  return setupFixture(isPhone, overrides).nativeElement as HTMLElement;
}

describe('AccountsListComponent sync status text', () => {
  it('shows "Sync failed" as row subtitle text on a phone, not by colour alone', () => {
    const text = setup(true, {syncStatus: 'failed'}).textContent ?? '';
    expect(text).toContain('0 accounts · Sync failed');
  });

  it('shows "Reconnect needed" for a reauth-required institution on a phone', () => {
    const text = setup(true, {syncStatus: 'reauth_required'}).textContent ?? '';
    expect(text).toContain('0 accounts · Reconnect needed');
  });

  it('shows "Syncing…" while a sync is in progress, even with a previous timestamp', () => {
    const element = setup(true, {
      syncStatus: 'syncing',
      lastSyncTimestamp: new Date().toISOString(),
    });
    const text = element.textContent ?? '';
    expect(text).toContain('0 accounts · Syncing…');
  });

  it('shows "Not synced yet" for a pending institution that never synced', () => {
    const text = setup(true, {syncStatus: 'pending'}).textContent ?? '';
    expect(text).toContain('0 accounts · Not synced yet');
    expect(text).not.toContain('synced never');
  });

  it('shows the bare time for a synced institution with a timestamp', () => {
    const element = setup(true, {
      syncStatus: 'synced',
      lastSyncTimestamp: new Date().toISOString(),
    });
    const text = element.textContent ?? '';
    expect(text).toContain('0 accounts · just now');
  });

  it('keeps the plain count subtitle off phone', () => {
    const text = setup(false).textContent ?? '';
    expect(text).toContain('0 accounts');
    expect(text).not.toContain('0 accounts · Sync failed');
  });
});

describe('AccountsListComponent net worth quality', () => {
  const headline = (overrides: Partial<Headline>): Headline => ({
    amount: 1000,
    percent: null,
    method: 'VALUE',
    scope: 'umbrella',
    window: {to: '2026-10-15'},
    quality: 'COMPLETE',
    reasons: [],
    currency: 'USD',
    ...overrides,
  });
  const header = (state: Parameters<typeof setupFixture>[3]): HTMLElement =>
    setupFixture(false, {}, [], {hasData: true, ...state}).nativeElement as HTMLElement;

  it('shows no badge for a complete total', () => {
    const el = header({totalNetWorth: 1000, headline: headline({})});
    expect(el.querySelector('[data-testid="headline-badge"]')).toBeNull();
  });

  it('badges a stale total', () => {
    const el = header({
      totalNetWorth: 1000,
      headline: headline({
        quality: 'DEGRADED',
        reasons: [{code: 'STALE_SOURCE', source: 'banking'}],
      }),
    });
    expect(el.querySelector('[data-testid="headline-badge"]')?.textContent?.trim()).toBe('Stale');
  });

  it('reads N/A with its badge, and keeps the card, when the total is withheld', () => {
    const el = header({
      totalNetWorth: 0,
      headline: headline({
        amount: null,
        quality: 'UNAVAILABLE',
        reasons: [{code: 'NOT_MEASURABLE'}],
      }),
    });
    expect(el.querySelector('[data-testid="accounts-net-worth"]')?.textContent?.trim()).toBe('N/A');
    expect(el.querySelector('[data-badge="unavailable"]')).not.toBeNull();
  });
});

describe('AccountsListComponent account row', () => {
  it('opens the transactions page with that account preselected', () => {
    const account: AccountBalanceItem = {
      accountId: 'acc-1',
      bankName: 'Test Bank',
      accountType: 'current',
      accountNumberLast4: '1234',
      currency: 'EUR',
      provider: 'truelayer',
      category: 'banking',
      currentBalance: 10,
      balanceInBaseCurrency: 10,
      syncStatus: 'synced',
      lastSyncTimestamp: null,
    };
    const fixture = setupFixture(false, {accounts: [account]});
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const row = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="account-row"]'
    );
    row?.click();

    expect(navigate).toHaveBeenCalledWith(['/transactions'], {queryParams: {account: 'acc-1'}});
  });
});

describe('AccountsListComponent investment nav rows', () => {
  function holding(accountId: string, symbol: string, accountType: string): AccountBalanceItem {
    return {
      accountId,
      bankName: 'Interactive Brokers',
      accountType,
      accountNumberLast4: symbol.slice(0, 4),
      currency: symbol,
      symbol: accountType === 'cash' ? null : symbol,
      provider: 'ibkr',
      category: 'brokerage',
      currentBalance: 40,
      balanceInBaseCurrency: 400,
      syncStatus: 'synced',
      lastSyncTimestamp: null,
    };
  }

  function brokerage(overrides: Partial<Institution> = {}): HTMLElement {
    const fixture = setupFixture(false, {
      provider: 'ibkr',
      name: 'Interactive Brokers',
      category: 'brokerage',
      syncStatus: 'synced',
      totalInBaseCurrency: 800,
      accounts: [
        holding('a1', 'GOOGL', 'brokerage'),
        holding('a2', 'BRK.B', 'brokerage'),
        holding('a3', 'USD Cash', 'cash'),
      ],
      ...overrides,
    });
    return fixture.nativeElement as HTMLElement;
  }

  it('renders one row per brokerage with its positions, cash and total, and no position rows', () => {
    const element = brokerage();

    expect(element.querySelectorAll('[data-testid="institution-nav-row"]')).toHaveLength(1);
    expect(
      element.querySelector('[data-testid="institution-nav-sublabel"]')?.textContent?.trim()
    ).toBe('2 positions · $400.00 cash');
    expect(element.querySelector('[data-testid="institution-nav-total"]')?.textContent).toContain(
      '$800.00'
    );
    expect(element.textContent).not.toContain('GOOGL');
    expect(element.querySelector('cmn-disclosure-row')).toBeNull();
  });

  it('opens Investments filtered to the provider', () => {
    const element = brokerage();

    const link = element.querySelector<HTMLAnchorElement>('[data-testid="institution-nav-link"]');
    expect(link?.getAttribute('href')).toBe('/accounts/investments?provider=ibkr');
  });

  it('keeps the actions menu outside the link, so both stay reachable', () => {
    const element = brokerage();

    const row = element.querySelector('[data-testid="institution-nav-row"]');
    expect(row?.querySelector('cmn-menu')).not.toBeNull();
    expect(row?.querySelector('a cmn-menu')).toBeNull();
  });

  it('leaves banking as an expanded disclosure row', () => {
    const element = setup(false, {syncStatus: 'synced'});

    expect(element.querySelector('cmn-disclosure-row')).not.toBeNull();
    expect(element.querySelector('[data-testid="institution-nav-row"]')).toBeNull();
  });
});

describe('AccountsListComponent sticky sections and remembered disclosure', () => {
  const KEY = 'truelayer:inst-1';

  function details(element: HTMLElement): HTMLDetailsElement {
    return element.querySelector('cmn-disclosure-row details') as HTMLDetailsElement;
  }

  it('renders each category as a list section with its subtotal in the header', () => {
    const element = setup(true, {syncStatus: 'synced'});

    const heading = element.querySelector('cmn-list-section h2');
    expect(heading?.textContent).toContain('Banks');
    expect(heading?.querySelector('[data-testid="section-subtotal"]')).not.toBeNull();
  });

  it('opens a banking group by default', () => {
    expect(details(setup(true, {syncStatus: 'synced'})).open).toBe(true);
  });

  it('opens a collapsed group closed, with its total still in the summary', () => {
    const fixture = setupFixture(true, {syncStatus: 'synced'}, [], {collapsed: [KEY]});
    const element = fixture.nativeElement as HTMLElement;

    expect(details(element).open).toBe(false);
    expect(details(element).querySelector('summary')?.textContent).toContain('$100.00');
  });

  it('records the open state when the user toggles the group', () => {
    const setInstitutionOpen = vi.fn();
    const fixture = setupFixture(true, {syncStatus: 'synced'}, [], {setInstitutionOpen});
    const group = details(fixture.nativeElement as HTMLElement);

    group.open = false;
    group.dispatchEvent(new Event('toggle'));

    expect(setInstitutionOpen).toHaveBeenCalledWith(KEY, false);
  });
});

describe('AccountsListComponent attention row and subtotals', () => {
  const THREE_DAYS_MS = 3 * 24 * 60 * 60 * 1000;

  it('shows a reconnect row at the top for an institution that needs reauth', () => {
    const frozenSince = new Date(Date.now() - THREE_DAYS_MS).toISOString();
    const element = setup(true, {
      name: 'AIB',
      syncStatus: 'reauth_required',
      lastSuccessfulSyncTimestamp: frozenSince,
    });

    const row = element.querySelector('[data-testid="attention-row"]');
    expect(row?.textContent).toContain('AIB needs reconnecting');
    expect(row?.textContent).toContain('Balances frozen since 3d ago');
    expect(row?.querySelector('cmn-button')?.textContent).toContain('Reconnect');
    expect(row?.className).toContain('sm:hidden');
  });

  it('shows no reconnect button on the attention row of a failed sync', () => {
    const element = setup(true, {name: 'Monzo', syncStatus: 'failed'});

    const row = element.querySelector('[data-testid="attention-row"]');
    expect(row?.textContent).toContain('Monzo sync failed');
    expect(row?.querySelector('cmn-button')).toBeNull();
  });

  it('shows no attention row for a healthy institution', () => {
    const element = setup(true, {syncStatus: 'synced'});

    expect(element.querySelector('[data-testid="attention-row"]')).toBeNull();
  });

  it('shows the section subtotal instead of an institution count', () => {
    const element = setup(false, {syncStatus: 'synced'});

    expect(element.querySelector('[data-testid="section-subtotal"]')?.textContent).toContain(
      '$100.00'
    );
    expect(element.textContent).not.toContain('1 bank');
  });
});

describe('AccountsListComponent reconnect and disconnect', () => {
  function setupActions(provider: string) {
    const connectStore = {openModal: vi.fn(), selectPickedProvider: vi.fn()};
    const dialog = {open: vi.fn().mockReturnValue({afterClosed: () => of(true)})};
    const fixture = setupFixture(false, {provider, syncStatus: 'reauth_required'}, [
      {provide: ConnectStore, useValue: connectStore},
      {provide: CmnDialogService, useValue: dialog},
    ]);
    const store = TestBed.inject(AccountsStore) as unknown as Record<
      string,
      ReturnType<typeof vi.fn>
    >;
    store['disconnectInzhur'] = vi.fn();
    return {cmp: fixture.componentInstance, connectStore, dialog, store};
  }

  it.each([
    ['truelayer', 'Reconnect bank'],
    ['inzhur', 'Reconnect Inzhur'],
  ])('reconnect() opens the %s sign-in as "%s"', (provider, title) => {
    const {cmp, connectStore, dialog} = setupActions(provider);

    expect(cmp.canReconnect({...baseInstitution, provider})).toBe(true);
    cmp.reconnect({provider});

    expect(connectStore.selectPickedProvider).toHaveBeenCalledWith(provider);
    expect(dialog.open).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({title}));
  });

  it('offers no reconnect for a provider that re-authorises outside the connect flow', () => {
    const {cmp, dialog} = setupActions('ibkr');

    expect(cmp.canReconnect({...baseInstitution, provider: 'ibkr'})).toBe(false);
    cmp.reconnect({provider: 'ibkr'});

    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('disconnecting the Inzhur row disconnects Inzhur only', () => {
    const {cmp, store} = setupActions('inzhur');

    cmp.disconnectInstitution({...baseInstitution, provider: 'inzhur', name: 'Inzhur'});

    expect(store['disconnectInzhur']).toHaveBeenCalled();
  });
});

describe('AccountsListComponent async states', () => {
  const render = (state: Parameters<typeof setupFixture>[3]): HTMLElement =>
    setupFixture(false, {}, [], state).nativeElement as HTMLElement;

  it('shows skeleton rows and no institutions while loading', () => {
    const el = render({isLoading: true});

    expect(el.querySelectorAll('cmn-skeleton').length).toBeGreaterThan(0);
    expect(el.querySelector('[data-testid="account-row"]')).toBeNull();
  });

  it('offers to connect the first account when none exist', () => {
    const el = render({isEmpty: true});

    expect(el.textContent).toContain('No accounts connected yet.');
    expect(el.textContent).toContain('Connect Your First Account');
  });

  it('shows the error with a Retry that reloads the accounts', () => {
    const load = vi.fn();
    const el = render({errorMessage: 'Failed to load accounts.', load});

    expect(el.querySelector('cmn-alert')?.textContent).toContain('Failed to load accounts.');
    const retry = Array.from(el.querySelectorAll<HTMLElement>('cmn-alert cmn-button')).find(b =>
      b.textContent?.includes('Retry')
    );
    retry?.querySelector('button')?.click();
    expect(load).toHaveBeenCalledOnce();
  });

  it('never renders an error as the empty state', () => {
    const el = render({errorMessage: 'Failed to load accounts.', isEmpty: false});

    expect(el.textContent).not.toContain('No accounts connected yet.');
  });

  it('keeps the loaded accounts on screen while a retry loads', () => {
    const el = render({isLoading: true, hasData: true});

    expect(el.querySelector('cmn-disclosure-row')).not.toBeNull();
  });

  it('shows the last data with a last-synced notice instead of an error while offline', () => {
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
    const el = render({errorMessage: 'Failed to load accounts.', hasData: true});

    expect(el.querySelector('[data-testid="offline-notice"]')?.textContent).toContain(
      "You're offline"
    );
    expect(el.textContent).not.toContain('Failed to load accounts.');
    expect(el.querySelector('cmn-disclosure-row')).not.toBeNull();
    vi.restoreAllMocks();
  });

  it('shows the institutions with no banner when loaded', () => {
    const el = render({});

    expect(el.querySelector('cmn-alert')).toBeNull();
    expect(el.textContent).not.toContain('No accounts connected yet.');
  });
});
