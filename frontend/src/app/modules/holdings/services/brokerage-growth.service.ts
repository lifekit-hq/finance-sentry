import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type BrokerageGrowth,
  type GrowthSleeve,
} from '../models/brokerage-growth/brokerage-growth.model';

@Injectable({providedIn: 'root'})
export class BrokerageGrowthService extends ApiService {
  constructor() {
    super('');
  }

  public getGrowth(sleeve: GrowthSleeve): Observable<BrokerageGrowth> {
    return this.get<BrokerageGrowth>(`wealth/${sleeve}/growth`);
  }
}
