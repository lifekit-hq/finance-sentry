import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  CANDIDATE_PAGE_SIZE,
  CANDIDATE_TRANSACTION_TYPE,
} from '../constants/commitment-candidate/commitment-candidate.constants';
import {type CommitmentCandidatesResponse} from '../models/commitment-candidate/commitment-candidate.model';

/** Recent outgoing transactions to pick a subscription or installment from. */
@Injectable({providedIn: 'root'})
export class CommitmentCandidatesService extends ApiService {
  constructor() {
    super('accounts');
  }

  public search(search: string): Observable<CommitmentCandidatesResponse> {
    const term = search.trim();
    return this.get<CommitmentCandidatesResponse>('transactions', {
      limit: CANDIDATE_PAGE_SIZE,
      transactionType: CANDIDATE_TRANSACTION_TYPE,
      ...(term ? {search: term} : {}),
    });
  }
}
