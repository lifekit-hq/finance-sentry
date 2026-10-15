import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type ScorecardDto} from '../../../shared/models/scorecard/scorecard.model';

/** One ticker's four axes and verdict word (spec 726 US2), read at request time; nothing is stored. */
@Injectable({providedIn: 'root'})
export class ScorecardService extends ApiService {
  constructor() {
    super('research/scorecards');
  }

  public read(symbol: string): Observable<ScorecardDto> {
    return this.get<ScorecardDto>(encodeURIComponent(symbol));
  }
}
