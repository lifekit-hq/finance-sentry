import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type RiskPicture} from '../models/risk-picture/risk-picture.model';

@Injectable({providedIn: 'root'})
export class RiskPictureService extends ApiService {
  constructor() {
    super('');
  }

  public getRiskPicture(): Observable<RiskPicture> {
    return this.get<RiskPicture>('radar/risk-picture');
  }
}
