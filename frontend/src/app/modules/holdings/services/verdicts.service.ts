import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type ScorecardVerdictSummary} from '../../../shared/models/scorecard/scorecard.model';

/**
 * The verdict word of many symbols in one call (spec 726 US2), for the Accounts position chips. Owner only
 * (`ai.use`): anyone else gets 403, so callers read it only behind that permission.
 */
@Injectable({providedIn: 'root'})
export class VerdictsService extends ApiService {
  constructor() {
    super('research/scorecards');
  }

  public getVerdicts(symbols: string[]): Observable<ScorecardVerdictSummary[]> {
    return this.get<ScorecardVerdictSummary[]>('verdicts', {symbols: symbols.join(',')});
  }
}
