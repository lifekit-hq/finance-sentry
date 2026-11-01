import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type PaydayTransfer,
  type PaydayTransferSettings,
} from '../models/payday-transfer/payday-transfer.model';

@Injectable({providedIn: 'root'})
export class PaydayTransferService extends ApiService {
  constructor() {
    super('payday-transfer');
  }

  public getPaydayTransfer(): Observable<PaydayTransfer> {
    return this.get<PaydayTransfer>();
  }

  public updateSettings(settings: PaydayTransferSettings): Observable<PaydayTransferSettings> {
    return this.put<PaydayTransferSettings>('settings', settings);
  }
}
