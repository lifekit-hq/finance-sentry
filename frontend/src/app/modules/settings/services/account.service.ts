import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type AccountClosure,
  type AccountClosureOptions,
  type CloseAccountRequest,
} from '../models/account-closure/account-closure.model';

/** The signed-in person's own account. */
@Injectable({providedIn: 'root'})
export class AccountService extends ApiService {
  constructor() {
    super('account');
  }

  public getClosureOptions(): Observable<AccountClosureOptions> {
    return this.get<AccountClosureOptions>('closure');
  }

  /** The person's own data as one JSON file: the signed-in person's rows only, no stored secrets. */
  public exportData(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/export`, {responseType: 'blob'});
  }

  public close(request: CloseAccountRequest): Observable<AccountClosure> {
    return this.post<AccountClosure>('close', request);
  }
}
