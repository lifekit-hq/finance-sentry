import {BreakpointObserver, type BreakpointState} from '@angular/cdk/layout';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, provideRouter} from '@angular/router';
import {of} from 'rxjs';

import {type Institution} from '../../../../shared/models/wealth/wealth.model';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {AccountsListComponent} from './accounts-list.component';

const failedInstitution: Institution = {
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

function setup(isPhone: boolean): HTMLElement {
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
          institutions: [failedInstitution],
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
    ],
  });
  const fixture = TestBed.createComponent(AccountsListComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('AccountsListComponent sync status text', () => {
  it('shows "Sync failed" as row subtitle text on a phone, not by colour alone', () => {
    const text = setup(true).textContent ?? '';
    expect(text).toContain('0 accounts · Sync failed');
  });

  it('keeps the plain count subtitle off phone', () => {
    const text = setup(false).textContent ?? '';
    expect(text).toContain('0 accounts');
    expect(text).not.toContain('0 accounts · Sync failed');
  });
});
