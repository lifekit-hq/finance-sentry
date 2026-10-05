import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type CommitmentAnchor} from '../models/commitment-candidate/commitment-candidate.model';
import {
  type AddCommitmentRequest,
  type LinkCommitmentRequest,
  type SubscriptionKind,
  type SubscriptionsListResponse,
  type SubscriptionSummary,
} from '../models/subscription/subscription.model';

@Injectable({providedIn: 'root'})
export class SubscriptionsService extends ApiService {
  constructor() {
    super('subscriptions');
  }

  public getSubscriptions(includeDismissed = false): Observable<SubscriptionsListResponse> {
    return this.get<SubscriptionsListResponse>(
      '',
      includeDismissed ? {includeDismissed: true} : undefined
    );
  }

  public getSummary(): Observable<SubscriptionSummary> {
    return this.get<SubscriptionSummary>('summary');
  }

  public getCommitmentAnchor(
    transactionId: string,
    kind: SubscriptionKind
  ): Observable<CommitmentAnchor> {
    return this.get<CommitmentAnchor>(`candidates/${transactionId}`, {kind});
  }

  public dismiss(id: string): Observable<void> {
    return this.patch<void>(`${id}/dismiss`);
  }

  public restore(id: string): Observable<void> {
    return this.patch<void>(`${id}/restore`);
  }

  public setInstallmentTerm(id: string, termCount: Nullable<number>): Observable<void> {
    return this.patch<void>(`installments/${id}/term`, {termCount});
  }

  public completeInstallment(id: string): Observable<void> {
    return this.patch<void>(`installments/${id}/complete`);
  }

  public deleteInstallment(id: string): Observable<void> {
    return this.delete<void>(`installments/${id}`);
  }

  public add(payload: AddCommitmentRequest): Observable<{id: string}> {
    return this.post<{id: string}>('', payload);
  }

  public link(id: string, payload: LinkCommitmentRequest): Observable<void> {
    return this.post<void>(`${id}/link`, payload);
  }
}
