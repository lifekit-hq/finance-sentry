import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type ConcentrationHistory,
  type RiskPicture,
} from '../models/risk-picture/risk-picture.model';

@Injectable({providedIn: 'root'})
export class RiskPictureService extends ApiService {
  constructor() {
    super('');
  }

  public getRiskPicture(): Observable<RiskPicture> {
    return this.get<RiskPicture>('radar/risk-picture');
  }

  public getConcentrationHistory(): Observable<ConcentrationHistory> {
    return this.get<ConcentrationHistory>('radar/risk-picture/history');
  }
}
