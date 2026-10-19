import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {CmnDialogService} from '@lifekit-hq/ui';
import {type Observable} from 'rxjs';

import {HoldingsStore} from '../../../holdings/store/holdings.store';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {AccountsListComponent} from './accounts-list.component';

function setup(lastSyncedAt: ReturnType<typeof signal<number | null>>) {
  let emissions = 0;
  const loadPositions = vi.fn((reloads: Observable<void>) => {
    reloads.subscribe(() => emissions++);
  });
  const store = {
    isLoading: signal(false),
    isEmpty: signal(false),
    errorMessage: signal(''),
    summary: signal(null),
    lastSyncedAt,
    openProvider: vi.fn(),
    institutionOpen: signal({}),
    closedAccounts: signal([]),
    accountNotes: signal({}),
    manualRows: signal([]),
    manualTotal: signal(0),
    baseCurrency: signal('USD'),
    categorySections: signal([]),
    netWorthBreakdown: signal([]),
    netWorthHeadline: signal(null),
    isNetWorthUnavailable: signal(false),
    totalNetWorth: signal(0),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: AccountsStore, useValue: store},
      {provide: ConnectStore, useValue: {}},
      {provide: CmnDialogService, useValue: {open: vi.fn()}},
    ],
  });
  TestBed.overrideComponent(AccountsListComponent, {
    set: {
      providers: [
        {provide: HoldingsStore, useValue: {positionsByProvider: signal({}), loadPositions}},
      ],
    },
  });
  TestBed.createComponent(AccountsListComponent).detectChanges();
  return {emissions: () => emissions};
}

describe('AccountsListComponent positions feed reload', () => {
  it('reloads the feed each time the summary reloads after its first load', () => {
    const lastSyncedAt = signal<number | null>(1);
    const feed = setup(lastSyncedAt);
    TestBed.tick();
    expect(feed.emissions()).toBe(0);

    lastSyncedAt.set(2);
    TestBed.tick();
    expect(feed.emissions()).toBe(1);

    lastSyncedAt.set(3);
    TestBed.tick();
    expect(feed.emissions()).toBe(2);
  });

  it('leaves the first summary load to the feed own initial load', () => {
    const lastSyncedAt = signal<number | null>(null);
    const feed = setup(lastSyncedAt);
    TestBed.tick();

    lastSyncedAt.set(1);
    TestBed.tick();

    expect(feed.emissions()).toBe(0);
  });
});
