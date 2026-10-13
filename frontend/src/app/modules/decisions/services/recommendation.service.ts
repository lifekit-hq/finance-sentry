import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type ConfirmRecommendationRequest,
  type RecommendationDto,
  type RecommendationsResult,
  type RejectRecommendationRequest,
} from '../../../shared/models/recommendation/recommendation.model';

/** The Decisions queue (#1069). Confirm only records the owner's decision; nothing here reaches a broker. */
@Injectable({providedIn: 'root'})
export class RecommendationService extends ApiService {
  constructor() {
    super('research/recommendations');
  }

  public list(): Observable<RecommendationsResult> {
    return this.get<RecommendationsResult>('');
  }

  public confirm(id: string, request: ConfirmRecommendationRequest): Observable<RecommendationDto> {
    return this.post<RecommendationDto>(`${encodeURIComponent(id)}/confirm`, request);
  }

  public reject(id: string, request: RejectRecommendationRequest): Observable<RecommendationDto> {
    return this.post<RecommendationDto>(`${encodeURIComponent(id)}/reject`, request);
  }
}
