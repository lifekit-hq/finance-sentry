import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, provideRouter, Router} from '@angular/router';
import {CmnDialogService} from '@lifekit-hq/ui';
import {of} from 'rxjs';

import {type Headline} from '../../../../shared/models/headline/headline.model';
import {
  type AccountBalanceItem,
  type Institution,
  type NetWorthBreakdownRow,
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
    open?: Record<string, boolean>;
    breakdown?: NetWorthBreakdownRow[];
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
    institutionOpen: signal(state.open ?? {}),
    setInstitutionOpen: state.setInstitutionOpen ?? vi.fn(),
    totalNetWorth: signal(state.totalNetWorth ?? 0),
    netWorthHeadline: signal(state.headline ?? null),
    isNetWorthUnavailable: signal(state.headline?.quality === 'UNAVAILABLE'),
    netWorthBreakdown: signal(state.breakdown ?? []),
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
      ...(extraProviders as never[]),
    ],
  });
  const fixture = TestBed.createComponent(AccountsListComponent);
  fixture.detectChanges();
  return fixture;
}

function setup(overrides: Partial<Institution> = {}): HTMLElement {
  return setupFixture(overrides).nativeElement as HTMLElement;
}

describe('AccountsListComponent sync status', () => {
  const dot = (element: HTMLElement): HTMLElement | null =>
    element.querySelector('[data-testid="avatar-status"]');

  it.each([
    ['failed', 'bg-status-error', '0 accounts · Sync failed'],
    ['reauth_required', 'bg-status-error', '0 accounts · Reconnect needed'],
    ['reconnect_needed', 'bg-status-error', '0 accounts · Reconnect needed'],
    ['pending', 'bg-status-warning', '0 accounts · Not synced yet'],
    ['dormant', 'bg-text-disabled', '0 accounts · Dormant'],
  ] as const)(
    'says a %s institution in words and marks it on the avatar',
    (syncStatus, colour, sublabel) => {
      const element = setup({syncStatus});

      expect(element.textContent).toContain(sublabel);
      expect(dot(element)?.className).toContain(colour);
    }
  );

  it.each([
    ['retrying', 'Retrying just now'],
    ['stale', 'Stale just now'],
  ] as const)('says how long ago a %s institution last synced', (syncStatus, note) => {
    const element = setup({syncStatus, lastSyncTimestamp: new Date().toISOString()});

    expect(element.textContent).toContain(`0 accounts · ${note}`);
    expect(dot(element)?.className).toContain('bg-status-warning');
  });

  it('keeps a healthy institution to the count and a green dot that names its last sync', () => {
    const element = setup({syncStatus: 'synced', lastSyncTimestamp: new Date().toISOString()});

    const sublabel = element.querySelector('cmn-disclosure-row summary')?.textContent ?? '';
    expect(sublabel).toContain('0 accounts');
    expect(sublabel).not.toContain('0 accounts ·');
    expect(dot(element)?.className).toContain('bg-status-success');
    expect(dot(element)?.getAttribute('aria-label')).toBe('Last synced just now');
  });

  it('adds no text while a sync is in progress, and says so on the dot', () => {
    const element = setup({
      syncStatus: 'syncing',
      lastSyncTimestamp: new Date().toISOString(),
    });

    expect(element.textContent).not.toContain('0 accounts ·');
    expect(dot(element)?.getAttribute('aria-label')).toBe('Syncing…');
  });

  it('gives a broken institution the same words on the dot as in the row', () => {
    expect(dot(setup({syncStatus: 'failed'}))?.getAttribute('aria-label')).toBe('Sync failed');
  });

  it('puts the status on the avatar, not beside the figure', () => {
    const element = setup({syncStatus: 'synced', lastSyncTimestamp: new Date().toISOString()});

    expect(element.querySelector('[data-testid="institution-health-label"]')).toBeNull();
    expect(dot(element)?.closest('cmn-institution-avatar')).not.toBeNull();
  });
});

describe('AccountsListComponent net worth breakdown', () => {
  const row = (label: string, value: number, percent: number | null): NetWorthBreakdownRow => ({
    label,
    color: '#000',
    value,
    institutionCount: 1,
    percent,
  });

  it('shows a negative category as a plain negative amount', () => {
    const fixture = setupFixture({}, [], {
      hasData: true,
      totalNetWorth: 9185.73,
      breakdown: [row('Brokerage', 9278.96, 100), row('Banking', -93.23, null)],
    });
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('-$93.23');
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
  const header = (state: Parameters<typeof setupFixture>[2]): HTMLElement =>
    setupFixture({}, [], {hasData: true, ...state}).nativeElement as HTMLElement;

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
    const fixture = setupFixture({accounts: [account]});
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const row = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="account-row"]'
    );
    row?.click();

    expect(navigate).toHaveBeenCalledWith(['/transactions'], {queryParams: {account: 'acc-1'}});
  });
});

describe('AccountsListComponent positions in a brokerage or exchange', () => {
  function holding(
    accountId: string,
    symbol: string,
    accountType: string,
    overrides: Partial<AccountBalanceItem> = {}
  ): AccountBalanceItem {
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
      ...overrides,
    };
  }

  function brokerage(state: Parameters<typeof setupFixture>[2] = {}): HTMLElement {
    const fixture = setupFixture(
      {
        provider: 'ibkr',
        name: 'Interactive Brokers',
        institutionId: 'ibkr-1',
        category: 'brokerage',
        syncStatus: 'synced',
        totalInBaseCurrency: 800,
        accounts: [
          holding('a1', 'GOOGL', 'brokerage', {currentBalance: 40, balanceInBaseCurrency: 6800.5}),
          holding('a2', 'BRK.B', 'brokerage', {currentBalance: 1}),
          holding('a3', 'USD', 'cash', {currentBalance: 400}),
        ],
      },
      [],
      state
    );
    return fixture.nativeElement as HTMLElement;
  }

  const details = (element: HTMLElement): HTMLDetailsElement =>
    element.querySelector('cmn-disclosure-row details') as HTMLDetailsElement;

  it('is one disclosure row with the compact sublabel and the total, like a bank', () => {
    const element = brokerage();

    expect(element.querySelectorAll('cmn-disclosure-row')).toHaveLength(1);
    expect(element.querySelector('cmn-disclosure-row summary')?.textContent).toContain(
      '2 positions · $400.00 cash'
    );
    expect(element.querySelector('[data-testid="institution-total"]')?.textContent).toContain(
      '$800.00'
    );
  });

  it('starts closed on a first visit', () => {
    expect(details(brokerage()).open).toBe(false);
  });

  it('opens when this device opened it', () => {
    expect(details(brokerage({open: {'ibkr:ibkr-1': true}})).open).toBe(true);
  });

  it('lists value-first position rows: ticker, size, then value', () => {
    const element = brokerage({open: {'ibkr:ibkr-1': true}});

    const rows = element.querySelectorAll('[data-testid="position-row"]');
    expect(rows).toHaveLength(2);
    expect(rows[0]?.querySelector('[data-testid="position-ticker"]')?.textContent).toBe('GOOGL');
    expect(rows[0]?.querySelector('[data-testid="position-quantity"]')?.textContent).toBe(
      '40 shares'
    );
    expect(rows[0]?.querySelector('[data-testid="position-value"]')?.textContent).toBe('$6,800.50');
  });

  it('keeps the cash line as a labelled Cash row, not a position', () => {
    const element = brokerage({open: {'ibkr:ibkr-1': true}});

    expect(element.querySelector('[data-testid="cash-label"]')?.textContent).toBe('Cash');
    expect(element.querySelectorAll('[data-testid="cash-row"]')).toHaveLength(1);
  });

  it('opens the asset dossier from a position row', () => {
    const fixture = setupFixture({
      provider: 'ibkr',
      category: 'brokerage',
      syncStatus: 'synced',
      accounts: [holding('a1', 'GOOGL', 'brokerage')],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('[data-testid="position-row"]')
      ?.click();

    expect(navigate).toHaveBeenCalledWith(['/assets', 'GOOGL']);
  });

  it('ends the list with a link to Investments filtered to the provider', () => {
    const link = brokerage().querySelector<HTMLAnchorElement>(
      '[data-testid="institution-investments-link"]'
    );

    expect(link?.getAttribute('href')).toBe('/accounts/investments?provider=ibkr');
  });

  it('leaves the actions menu on the row', () => {
    expect(brokerage().querySelector('cmn-disclosure-row cmn-menu')).not.toBeNull();
  });

  it('gives a bank account list no position rows or Investments link', () => {
    const element = setup({syncStatus: 'synced'});

    expect(element.querySelector('[data-testid="position-row"]')).toBeNull();
    expect(element.querySelector('[data-testid="institution-investments-link"]')).toBeNull();
  });
});

describe('AccountsListComponent sticky sections and remembered disclosure', () => {
  const KEY = 'truelayer:inst-1';

  function details(element: HTMLElement): HTMLDetailsElement {
    return element.querySelector('cmn-disclosure-row details') as HTMLDetailsElement;
  }

  it('renders each category as a list section with its subtotal in the header', () => {
    const element = setup({syncStatus: 'synced'});

    const heading = element.querySelector('cmn-list-section h2');
    expect(heading?.textContent).toContain('Banks');
    expect(heading?.querySelector('[data-testid="section-subtotal"]')).not.toBeNull();
  });

  it('opens a banking group by default', () => {
    expect(details(setup({syncStatus: 'synced'})).open).toBe(true);
  });

  it('opens a collapsed group closed, with its total still in the summary', () => {
    const fixture = setupFixture({syncStatus: 'synced'}, [], {open: {[KEY]: false}});
    const element = fixture.nativeElement as HTMLElement;

    expect(details(element).open).toBe(false);
    expect(details(element).querySelector('summary')?.textContent).toContain('$100.00');
  });

  it('records the open state when the user toggles the group', () => {
    const setInstitutionOpen = vi.fn();
    const fixture = setupFixture({syncStatus: 'synced'}, [], {setInstitutionOpen});
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
    const element = setup({
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
    const element = setup({name: 'Monzo', syncStatus: 'failed'});

    const row = element.querySelector('[data-testid="attention-row"]');
    expect(row?.textContent).toContain('Monzo sync failed');
    expect(row?.querySelector('cmn-button')).toBeNull();
  });

  it('shows no attention row for a healthy institution', () => {
    const element = setup({syncStatus: 'synced'});

    expect(element.querySelector('[data-testid="attention-row"]')).toBeNull();
  });

  it('shows the section subtotal instead of an institution count', () => {
    const element = setup({syncStatus: 'synced'});

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
    const fixture = setupFixture({provider, syncStatus: 'reauth_required'}, [
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
  const render = (state: Parameters<typeof setupFixture>[2]): HTMLElement =>
    setupFixture({}, [], state).nativeElement as HTMLElement;

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

describe('AccountsListComponent connection-health labels', () => {
  const dormantAccount: AccountBalanceItem = {
    accountId: 'acc-eaid',
    bankName: 'Monobank',
    accountType: 'current',
    accountNumberLast4: '4321',
    currency: 'UAH',
    provider: 'monobank',
    category: 'banking',
    currentBalance: 0,
    balanceInBaseCurrency: 0,
    syncStatus: 'dormant',
    lastSyncTimestamp: null,
  };

  it('asks for a reconnect on the attention row and in the row, as for a reauth', () => {
    const element = setup({name: 'AIB', syncStatus: 'reconnect_needed'});

    const row = element.querySelector('[data-testid="attention-row"]');
    expect(row?.textContent).toContain('AIB needs reconnecting');
    expect(row?.querySelector('cmn-button')?.textContent).toContain('Reconnect');
  });

  it.each(['retrying', 'dormant'] as const)(
    'raises no attention row for a %s institution',
    syncStatus => {
      expect(setup({syncStatus}).querySelector('[data-testid="attention-row"]')).toBeNull();
    }
  );

  it('labels a Dormant account quietly in its row', () => {
    const element = setupFixture({
      provider: 'monobank',
      syncStatus: 'synced',
      accounts: [dormantAccount],
    }).nativeElement as HTMLElement;

    expect(element.querySelector('[data-testid="account-dormant-label"]')?.textContent).toContain(
      'Dormant'
    );
  });
});

describe('AccountsListComponent reconnect deep link', () => {
  function openWithReconnect(provider: string) {
    const connectStore = {openModal: vi.fn(), selectPickedProvider: vi.fn()};
    const dialog = {open: vi.fn().mockReturnValue({afterClosed: () => of(true)})};
    const navigate = vi.fn().mockResolvedValue(true);
    setupFixture({provider, syncStatus: 'reconnect_needed'}, [
      {provide: ConnectStore, useValue: connectStore},
      {provide: CmnDialogService, useValue: dialog},
      {provide: Router, useValue: {navigate}},
      {
        provide: ActivatedRoute,
        useValue: {snapshot: {queryParamMap: new Map([['reconnect', provider]])}},
      },
    ]);
    return {connectStore, dialog, navigate};
  }

  it("opens the provider's existing reconnect flow from ?reconnect=", () => {
    const {connectStore, dialog} = openWithReconnect('truelayer');

    expect(connectStore.selectPickedProvider).toHaveBeenCalledWith('truelayer');
    expect(dialog.open).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({title: 'Reconnect bank'})
    );
  });

  it('lands on the list, with no new sign-in, for a provider that has no reconnect flow', () => {
    const {dialog} = openWithReconnect('ibkr');

    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('drops the parameter so a refresh does not reopen the flow', () => {
    const {navigate} = openWithReconnect('truelayer');

    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({
        queryParams: expect.objectContaining({reconnect: null}),
        replaceUrl: true,
      })
    );
  });
});
