import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type ConnectInzhurSessionRequest,
  type InzhurConnectionStatus,
  type InzhurConnectResult,
} from '../models/inzhur/inzhur.model';

@Injectable({providedIn: 'root'})
export class InzhurService extends ApiService {
  constructor() {
    super('brokerage/inzhur');
  }

  public getStatus(): Observable<InzhurConnectionStatus> {
    return this.get<InzhurConnectionStatus>('status');
  }

  /** Hands over the session the owner signed in to on inzhur.reit; the backend refreshes it once. */
  public connectSession(payload: ConnectInzhurSessionRequest): Observable<InzhurConnectResult> {
    return this.post<InzhurConnectResult>('session', payload);
  }

  public disconnect(): Observable<void> {
    return this.delete<void>('disconnect');
  }
}
