import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type WealthSummaryResponse} from '../../../shared/models/wealth/wealth.model';
import {type FireProjection} from '../models/fire/fire.model';

@Injectable({providedIn: 'root'})
export class WealthService extends ApiService {
  constructor() {
    super('wealth');
  }

  public getSummary(): Observable<WealthSummaryResponse> {
    return this.get<WealthSummaryResponse>('summary');
  }

  public getFireProjection(): Observable<FireProjection> {
    return this.get<FireProjection>('fire');
  }
}
