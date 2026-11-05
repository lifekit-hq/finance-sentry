import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type BrokeragePnl} from '../models/brokerage-pnl/brokerage-pnl.model';

@Injectable({providedIn: 'root'})
export class BrokeragePnlService extends ApiService {
  constructor() {
    super('');
  }

  public getPnl(): Observable<BrokeragePnl> {
    return this.get<BrokeragePnl>('wealth/brokerage/pnl');
  }
}
