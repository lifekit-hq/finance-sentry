import {BreakpointObserver, type BreakpointState} from '@angular/cdk/layout';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, provideRouter, Router} from '@angular/router';
import {CmnDialogService} from '@lifekit-hq/ui';
import {of} from 'rxjs';

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
  extraProviders: unknown[] = []
) {
  const institution: Institution = {...baseInstitution, ...overrides};
  const store = {
    isLoading: signal(false),
    isEmpty: signal(false),
    errorMessage: signal(null),
    totalNetWorth: signal(0),
    netWorthBreakdown: signal([]),
    baseCurrency: signal('USD'),
    categorySections: signal([
      {
        category: 'banking',
        title: 'Banks',
        institutionNoun: 'bank',
        rowNoun: 'account',
        summary: {
          category: 'banking',
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

  it('shows "synced <time>" for a synced institution with a timestamp', () => {
    const element = setup(true, {
      syncStatus: 'synced',
      lastSyncTimestamp: new Date().toISOString(),
    });
    const text = element.textContent ?? '';
    expect(text).toContain('0 accounts · synced just now');
  });

  it('keeps the plain count subtitle off phone', () => {
    const text = setup(false).textContent ?? '';
    expect(text).toContain('0 accounts');
    expect(text).not.toContain('0 accounts · Sync failed');
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
