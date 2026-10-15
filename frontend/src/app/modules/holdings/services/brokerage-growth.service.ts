import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type BrokerageGrowth} from '../models/brokerage-growth/brokerage-growth.model';

@Injectable({providedIn: 'root'})
export class BrokerageGrowthService extends ApiService {
  constructor() {
    super('');
  }

  public getGrowth(): Observable<BrokerageGrowth> {
    return this.get<BrokerageGrowth>('wealth/brokerage/growth');
  }
}
