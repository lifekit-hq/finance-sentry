import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type GrowthSleeve} from '@shared/models/brokerage-growth/brokerage-growth.model';
import {type Observable} from 'rxjs';

import {type SleeveCosts} from '../models/cost-of-investing/cost-of-investing.model';

@Injectable({providedIn: 'root'})
export class CostOfInvestingService extends ApiService {
  constructor() {
    super('');
  }

  public getCosts(sleeve: GrowthSleeve): Observable<SleeveCosts> {
    return this.get<SleeveCosts>(`wealth/${sleeve}/costs`);
  }
}
