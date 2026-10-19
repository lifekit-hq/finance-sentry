import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type WealthSummaryResponse} from '../../../shared/models/wealth/wealth.model';
import {type FireProjection} from '../models/fire/fire.model';
import {type MonthlyIncomeLens} from '../models/income-lens/income-lens.model';
import {
  type ManualHolding,
  type ManualHoldingInput,
} from '../models/manual-holding/manual-holding.model';
import {type NetWorthProjection} from '../models/projection/projection.model';

const MANUAL_HOLDINGS_PATH = 'manual-holdings';

@Injectable({providedIn: 'root'})
export class WealthService extends ApiService {
  constructor() {
    super('wealth');
  }

  public getSummary(): Observable<WealthSummaryResponse> {
    return this.get<WealthSummaryResponse>('summary');
  }

  public getFireProjection(): Observable<FireProjection> {
    return this.get<FireProjection>('fire');
  }

  public getNetWorthProjection(): Observable<NetWorthProjection> {
    return this.get<NetWorthProjection>('projection');
  }

  public getMonthlyIncomeLens(): Observable<MonthlyIncomeLens> {
    return this.get<MonthlyIncomeLens>('income-lens');
  }

  public getManualHoldings(): Observable<ManualHolding[]> {
    return this.get<ManualHolding[]>(MANUAL_HOLDINGS_PATH);
  }

  public createManualHolding(input: ManualHoldingInput): Observable<ManualHolding> {
    return this.post<ManualHolding>(MANUAL_HOLDINGS_PATH, input);
  }

  public updateManualHolding(id: string, input: ManualHoldingInput): Observable<ManualHolding> {
    return this.put<ManualHolding>(`${MANUAL_HOLDINGS_PATH}/${id}`, input);
  }

  public deleteManualHolding(id: string): Observable<void> {
    return this.delete<void>(`${MANUAL_HOLDINGS_PATH}/${id}`);
  }
}
