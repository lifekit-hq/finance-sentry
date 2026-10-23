import {HttpParams} from '@angular/common/http';
import {Injectable} from '@angular/core';
import {ApiService, poll} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {Observable} from 'rxjs';

import {environment} from '../../../../environments/environment';
import {type DateRange, DateRangeUtils} from '../../../shared/utils/date-range.utils';
import {AccountsResponse, ConnectMonobankResponse} from '../models/bank-account/bank-account.model';
import {
  DashboardData,
  type HistoryRange,
  type NetWorthHistoryResponse,
} from '../models/dashboard/dashboard.model';
import {type SpendingSummary} from '../models/dashboard/spending-summary.model';
import {type FlowBreakdown} from '../models/flow-breakdown/flow-breakdown.model';
import {type NetWorthBridgeResponse} from '../models/net-worth-bridge/net-worth-bridge.model';
import {SyncStatusResponse, TriggerSyncResponse} from '../models/sync/sync.model';
import {
  type GetAllTransactionsParams,
  type GlobalTransactionsResponse,
} from '../models/transaction/transaction.model';
import {
  type BeginTrueLayerConnectRequest,
  type BeginTrueLayerConnectResponse,
  type TrueLayerProvider,
} from '../models/truelayer/truelayer.model';

export type {DashboardData, SyncStatusResponse, TriggerSyncResponse};

const DEFAULT_SYNC_POLL_INTERVAL_MS = 2000;

@Injectable({providedIn: 'root'})
export class BankSyncService extends ApiService {
  constructor() {
    super('accounts');
  }

  /** The base builder drops arrays; a list value becomes one repeated query key per item. */
  protected override buildParams(input?: object): HttpParams {
    let params = super.buildParams(input);
    for (const [key, value] of Object.entries(input ?? {})) {
      if (Array.isArray(value)) {
        for (const item of value) {
          params = params.append(key, String(item));
        }
      }
    }
    return params;
  }

  public connectMonobank(token: string): Observable<ConnectMonobankResponse> {
    return this.post<ConnectMonobankResponse>('monobank/connect', {token});
  }

  public listTrueLayerProviders(country: string): Observable<TrueLayerProvider[]> {
    return this.get<TrueLayerProvider[]>('truelayer/providers', {country});
  }

  public beginTrueLayerConnect(
    request: BeginTrueLayerConnectRequest
  ): Observable<BeginTrueLayerConnectResponse> {
    return this.post<BeginTrueLayerConnectResponse>('truelayer/connect', request);
  }

  public getAccounts(status?: string, currency?: string): Observable<AccountsResponse> {
    return this.get<AccountsResponse>('', {status, currency});
  }

  public getAllTransactions(
    params?: GetAllTransactionsParams
  ): Observable<GlobalTransactionsResponse> {
    return this.get<GlobalTransactionsResponse>('transactions', params);
  }

  public triggerSync(accountId: string): Observable<TriggerSyncResponse> {
    return this.post<TriggerSyncResponse>(`${accountId}/sync`);
  }

  public getSyncStatus(accountId: string): Observable<SyncStatusResponse> {
    return this.get<SyncStatusResponse>(`${accountId}/sync-status`);
  }

  public pollSyncStatus(
    accountId: string,
    intervalMs = DEFAULT_SYNC_POLL_INTERVAL_MS
  ): Observable<SyncStatusResponse> {
    return poll(() => this.getSyncStatus(accountId), {
      intervalMs,
      isDone: s => s.status === 'success' || s.status === 'failed',
    });
  }

  public disconnectAccount(accountId: string): Observable<void> {
    return this.delete<void>(accountId);
  }

  /** Brings a retired account back; the next sync lists and refreshes it again. */
  public reopenAccount(accountId: string): Observable<void> {
    return this.post<void>(`${accountId}/reopen`);
  }

  public disconnectMonobank(): Observable<void> {
    return this.delete<void>('monobank');
  }

  public disconnectInstitution(provider: string, institutionId: string): Observable<void> {
    return this.delete<void>(`institutions/${provider}/${institutionId}`);
  }

  /**
   * `windowFrom` (`YYYY-MM-DD`, UTC) is the day-resolution window start (1W, MTD, 1M); the
   * backend then also returns `windowFlow` and scopes the top categories from that day.
   */
  public getDashboardData(
    months?: number,
    windowMonths?: number,
    windowFrom?: string
  ): Observable<DashboardData> {
    const params: Record<string, number | string> = {};
    if (months !== undefined) {
      params['months'] = months;
    }
    if (windowMonths !== undefined) {
      params['windowMonths'] = windowMonths;
    }
    if (windowFrom !== undefined) {
      params['windowFrom'] = windowFrom;
    }
    const options = {params};
    return this.http.get<DashboardData>(`${environment.apiBaseUrl}/dashboard/aggregated`, options);
  }

  public getFlowBreakdown(month: string, months?: number): Observable<FlowBreakdown> {
    const params: Record<string, string | number> =
      months === undefined ? {month} : {month, months};
    return this.http.get<FlowBreakdown>(`${environment.apiBaseUrl}/dashboard/flow-breakdown`, {
      params,
    });
  }

  /** A dashboard window's day range (UTC, inclusive); a null `from` is open-ended. */
  public getFlowBreakdownRange(
    from: Nullable<string>,
    to: string,
    months: number
  ): Observable<FlowBreakdown> {
    const params: Record<string, string | number> = from ? {from, to, months} : {to, months};
    return this.http.get<FlowBreakdown>(`${environment.apiBaseUrl}/dashboard/flow-breakdown`, {
      params,
    });
  }

  /** Out and top category for one account (all when null) over a day range; null ends are open. */
  public getSpendingSummary(scope: {
    accountId: Nullable<string>;
    from: Nullable<string>;
    to: Nullable<string>;
  }): Observable<SpendingSummary> {
    const params: Record<string, string> = {};
    if (scope.accountId) {
      params['accountId'] = scope.accountId;
    }
    if (scope.from) {
      params['from'] = scope.from;
    }
    if (scope.to) {
      params['to'] = scope.to;
    }
    return this.http.get<SpendingSummary>(`${environment.apiBaseUrl}/dashboard/spending-summary`, {
      params,
    });
  }

  public getNetWorthHistory(range: HistoryRange): Observable<NetWorthHistoryResponse> {
    const params = DateRangeUtils.toHttpParams(DateRangeUtils.fromRelativeRange(range));
    return this.http.get<NetWorthHistoryResponse>(`${environment.apiBaseUrl}/net-worth/history`, {
      params,
    });
  }

  /** The net-worth bridge (M5) over a window of days; an open `from` starts at the first snapshot. */
  public getNetWorthBridge(days: DateRange): Observable<NetWorthBridgeResponse> {
    const params = DateRangeUtils.toHttpParams(days);
    return this.http.get<NetWorthBridgeResponse>(`${environment.apiBaseUrl}/net-worth/bridge`, {
      params,
    });
  }
}
