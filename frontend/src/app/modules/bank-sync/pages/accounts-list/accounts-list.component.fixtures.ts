import {signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {ActivatedRoute, provideRouter} from '@angular/router';
import {type Nullable} from '@lifekit-hq/ui';
import {NEVER} from 'rxjs';

import {type Headline} from '../../../../shared/models/headline/headline.model';
import {
  type Institution,
  type NetWorthBreakdownRow,
} from '../../../../shared/models/wealth/wealth.model';
import {BookPerformanceService} from '../../../holdings/services/book-performance.service';
import {BrokerageGrowthService} from '../../../holdings/services/brokerage-growth.service';
import {type PositionRow} from '../../../holdings/store/holdings.computed';
import {HoldingsStore} from '../../../holdings/store/holdings.store';
import {
  type AccountNote,
  type ClosedAccountRow,
} from '../../models/account-lifecycle/account-lifecycle.model';
import {type ManualHoldingRow} from '../../models/manual-holding/manual-holding.model';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {AccountsListComponent} from './accounts-list.component';

export const baseInstitution: Institution = {
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

export function setupFixture(
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
    closed?: ClosedAccountRow[];
    notes?: Record<string, AccountNote>;
    manual?: ManualHoldingRow[];
    positions?: Record<string, PositionRow[]>;
    openProvider?: (provider: Nullable<string>) => void;
    baseCurrency?: string;
  } = {}
): ComponentFixture<AccountsListComponent> {
  const institution: Institution = {...baseInstitution, ...overrides};
  const store = {
    isLoading: signal(state.isLoading ?? false),
    isEmpty: signal(state.isEmpty ?? false),
    errorMessage: signal(state.errorMessage ?? ''),
    summary: signal(state.hasData ? {} : null),
    lastSyncedAt: signal<number | null>(state.hasData ? Date.now() : null),
    load: state.load ?? vi.fn(),
    closedAccounts: signal(state.closed ?? []),
    accountNotes: signal(state.notes ?? {}),
    manualRows: signal(state.manual ?? []),
    manualTotal: signal((state.manual ?? []).reduce((sum, row) => sum + row.amount, 0)),
    saveManualHolding: vi.fn(),
    removeManualHolding: vi.fn(),
    reopenAccount: vi.fn(),
    disconnectAccount: vi.fn(),
    institutionOpen: signal(state.open ?? {}),
    setInstitutionOpen: state.setInstitutionOpen ?? vi.fn(),
    openProvider: state.openProvider ?? vi.fn(),
    totalNetWorth: signal(state.totalNetWorth ?? 0),
    netWorthHeadline: signal(state.headline ?? null),
    isNetWorthUnavailable: signal(state.headline?.quality === 'UNAVAILABLE'),
    netWorthBreakdown: signal(state.breakdown ?? []),
    baseCurrency: signal(state.baseCurrency ?? 'USD'),
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
      // The growth sections under brokerage and crypto load their own feeds; they stay loading here.
      {provide: BrokerageGrowthService, useValue: {getGrowth: () => NEVER}},
      {provide: BookPerformanceService, useValue: {getPerformance: () => NEVER}},
      ...(extraProviders as never[]),
    ],
  });
  TestBed.overrideComponent(AccountsListComponent, {
    set: {
      providers: [
        {
          provide: HoldingsStore,
          useValue: {
            positionsByProvider: signal(state.positions ?? {}),
            loadPositions: vi.fn(),
          },
        },
      ],
    },
  });
  const fixture = TestBed.createComponent(AccountsListComponent);
  fixture.detectChanges();
  return fixture;
}

export function setup(overrides: Partial<Institution> = {}): HTMLElement {
  return setupFixture(overrides).nativeElement as HTMLElement;
}
