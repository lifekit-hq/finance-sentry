import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type ConnectRevolutXRequest,
  type ConnectRevolutXResponse,
} from '../models/revolut-x/revolut-x.model';

@Injectable({providedIn: 'root'})
export class RevolutXService extends ApiService {
  constructor() {
    super('crypto/revolut-x');
  }

  public connect(request: ConnectRevolutXRequest): Observable<ConnectRevolutXResponse> {
    return this.post<ConnectRevolutXResponse>('connect', request);
  }

  public disconnect(): Observable<void> {
    return this.delete<void>('disconnect');
  }
}
