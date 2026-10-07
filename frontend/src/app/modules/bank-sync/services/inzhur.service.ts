import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type InzhurConnectionStatus,
  type InzhurConnectResult,
  type StartInzhurLoginRequest,
  type VerifyInzhurLoginRequest,
} from '../models/inzhur/inzhur.model';

@Injectable({providedIn: 'root'})
export class InzhurService extends ApiService {
  constructor() {
    super('brokerage/inzhur');
  }

  public getStatus(): Observable<InzhurConnectionStatus> {
    return this.get<InzhurConnectionStatus>('status');
  }

  /** May make Inzhur send the owner an SMS; capped server-side per day. */
  public startLogin(payload: StartInzhurLoginRequest): Observable<InzhurConnectResult> {
    return this.post<InzhurConnectResult>('login/start', payload);
  }

  public verifyLogin(payload: VerifyInzhurLoginRequest): Observable<InzhurConnectResult> {
    return this.post<InzhurConnectResult>('login/verify', payload);
  }

  public disconnect(): Observable<void> {
    return this.delete<void>('disconnect');
  }
}
