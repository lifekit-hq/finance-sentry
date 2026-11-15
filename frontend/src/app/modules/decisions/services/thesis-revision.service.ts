import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type ConfirmThesisRevisionRequest,
  type DecideThesisRevisionResult,
  type RejectThesisRevisionRequest,
  type ThesisRevisionsResult,
} from '../../../shared/models/thesis-revision/thesis-revision.model';

/**
 * The owner's confirm surface for thesis revisions Ledger proposes. Confirming writes the thesis (a promotion runs
 * the risk gate first); it never places or changes a position.
 */
@Injectable({providedIn: 'root'})
export class ThesisRevisionService extends ApiService {
  constructor() {
    super('research/thesis-revisions');
  }

  public list(): Observable<ThesisRevisionsResult> {
    return this.get<ThesisRevisionsResult>('');
  }

  public confirm(
    id: string,
    request: ConfirmThesisRevisionRequest
  ): Observable<DecideThesisRevisionResult> {
    return this.post<DecideThesisRevisionResult>(`${encodeURIComponent(id)}/confirm`, request);
  }

  public reject(
    id: string,
    request: RejectThesisRevisionRequest
  ): Observable<DecideThesisRevisionResult> {
    return this.post<DecideThesisRevisionResult>(`${encodeURIComponent(id)}/reject`, request);
  }
}
