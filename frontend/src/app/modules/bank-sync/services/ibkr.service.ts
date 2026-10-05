import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type ConnectIbkrFlexRequest,
  type ConnectIBKRRequest,
  type IBKRConnectResult,
  type IbkrFlexPreview,
} from '../models/ibkr/ibkr.model';

@Injectable({providedIn: 'root'})
export class IBKRService extends ApiService {
  constructor() {
    super('brokerage/ibkr');
  }

  public connect(payload: ConnectIBKRRequest): Observable<IBKRConnectResult> {
    return this.post<IBKRConnectResult>('connect', payload);
  }

  public validateFlex(payload: ConnectIbkrFlexRequest): Observable<IbkrFlexPreview> {
    return this.post<IbkrFlexPreview>('flex/validate', payload);
  }

  public connectFlex(payload: ConnectIbkrFlexRequest): Observable<void> {
    return this.post<void>('flex/connect', payload);
  }

  public disconnect(): Observable<void> {
    return this.delete<void>('disconnect');
  }
}
