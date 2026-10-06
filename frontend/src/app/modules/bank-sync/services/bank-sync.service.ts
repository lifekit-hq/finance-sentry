import {HttpParams} from '@angular/common/http';
import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {Observable, timer} from 'rxjs';
import {shareReplay, switchMap, takeWhile} from 'rxjs/operators';

import {environment} from '../../../../environments/environment';
import {DateRangeUtils} from '../../../shared/utils/date-range.utils';
import {AccountsResponse, ConnectMonobankResponse} from '../models/bank-account/bank-account.model';
import {
  DashboardData,
  type HistoryRange,
  type NetWorthHistoryResponse,
} from '../models/dashboard/dashboard.model';
import {type FlowBreakdown} from '../models/flow-breakdown/flow-breakdown.model';
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
    // ApiService.get drops array values; HttpParams serialises them as repeated params.
    const fromObject: Record<string, string | number | readonly string[]> = {};
    for (const [key, value] of Object.entries(params ?? {})) {
      if (value !== undefined) {
        fromObject[key] = value as string | number | readonly string[];
      }
    }
    return this.http.get<GlobalTransactionsResponse>(`${this.baseUrl}/transactions`, {
      params: new HttpParams({fromObject}),
    });
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
    return timer(0, intervalMs).pipe(
      switchMap(() => this.getSyncStatus(accountId)),
      takeWhile(s => s.status !== 'success' && s.status !== 'failed', true),
      shareReplay(1)
    );
  }

  public disconnectAccount(accountId: string): Observable<void> {
    return this.delete<void>(accountId);
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

  public getNetWorthHistory(range: HistoryRange): Observable<NetWorthHistoryResponse> {
    const params = DateRangeUtils.toHttpParams(DateRangeUtils.fromRelativeRange(range));
    return this.http.get<NetWorthHistoryResponse>(`${environment.apiBaseUrl}/net-worth/history`, {
      params,
    });
  }
}
