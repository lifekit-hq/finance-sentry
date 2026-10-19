import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';

import {type MaybeHeadline} from '../../../../shared/models/headline/headline.model';
import {
  type CategoryStat,
  type DashboardData,
  type HistoryRange,
  type MonthlyFlow,
  type NetWorthScopeChangeDto,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';
import {dashboardComputed} from './dashboard.computed';

export interface Fixture {
  monthlyFlow: MonthlyFlow[];
  windowFlow?: MonthlyFlow[];
  totalNetWorthUsd?: number;
  netWorthHistory?: NetWorthSnapshotDto[];
  baseCurrency?: string;
  topCategories?: CategoryStat[];
  scrubIndex?: number | null;
  scopeChanges?: NetWorthScopeChangeDto[];
  cashUsd?: number | null;
  investedUsd?: number | null;
  netWorthHeadline?: MaybeHeadline;
  cashHeadline?: MaybeHeadline;
  investedHeadline?: MaybeHeadline;
  changeHeadline?: MaybeHeadline;
}

export function build(
  {
    monthlyFlow,
    windowFlow,
    totalNetWorthUsd = 0,
    netWorthHistory = [],
    baseCurrency,
    topCategories = [],
    scrubIndex = null,
    scopeChanges = [],
    cashUsd,
    investedUsd,
    netWorthHeadline,
    cashHeadline,
    investedHeadline,
    changeHeadline,
  }: Fixture,
  historyRange: HistoryRange = '3m'
) {
  const data: DashboardData = {
    aggregatedBalance: {USD: 0},
    totalNetWorthUsd,
    accountCount: 1,
    accountsByType: {},
    monthlyFlow,
    windowFlow,
    topCategories,
    lastSyncTimestamp: null,
    baseCurrency,
    cashUsd,
    investedUsd,
    netWorthHeadline,
    cashHeadline,
    investedHeadline,
  } as unknown as DashboardData;

  return {
    data: signal<Nullable<DashboardData>>(data),
    dataRange: signal<Nullable<HistoryRange>>(historyRange),
    historyRange: signal<HistoryRange>(historyRange),
    netWorthHistory: signal<NetWorthSnapshotDto[]>(netWorthHistory),
    historyLoadedRange: signal<Nullable<HistoryRange>>(historyRange),
    isLoading: signal(false),
    hasError: signal(false),
    historyLoading: signal(false),
    historyError: signal<string | null>(null),
    scrubIndex: signal<number | null>(scrubIndex),
    historyScopeChanges: signal<NetWorthScopeChangeDto[]>(scopeChanges),
    historyChangeHeadline: signal<MaybeHeadline>(changeHeadline),
  };
}

export function dashboardFor(fixture: Fixture) {
  return TestBed.runInInjectionContext(() => dashboardComputed(build(fixture)));
}

export function snapshot(banking: number, brokerage: number, crypto: number): NetWorthSnapshotDto {
  return {
    snapshotDate: '2026-07-31',
    bankingTotal: banking,
    brokerageTotal: brokerage,
    cryptoTotal: crypto,
    totalNetWorth: banking + brokerage + crypto,
    currency: 'USD',
  };
}
