import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {map, type Observable} from 'rxjs';

import {
  type Institution,
  type WealthSummaryResponse,
} from '../../../shared/models/wealth/wealth.model';
import {type FireProjection} from '../models/fire/fire.model';

@Injectable({providedIn: 'root'})
export class WealthService extends ApiService {
  constructor() {
    super('wealth');
  }

  /**
   * Accounts still shows only what it always did: Monobank's empty pockets and cards stay out until the
   * "N hidden" row and the visibility settings land. The API now returns them flagged instead of dropping them.
   */
  private static withoutEmptyCards(institution: Institution): Institution {
    if (!institution.cards) {
      return institution;
    }
    return {
      ...institution,
      cards: institution.cards
        .filter(card => card.hiddenReason !== 'empty')
        .map(card => ({...card, accounts: card.accounts.filter(a => a.hiddenReason !== 'empty')})),
    };
  }

  public getSummary(): Observable<WealthSummaryResponse> {
    return this.get<WealthSummaryResponse>('summary').pipe(
      map(summary => ({
        ...summary,
        categories: summary.categories.map(category => ({
          ...category,
          institutions: category.institutions.map(WealthService.withoutEmptyCards),
        })),
      }))
    );
  }

  public getFireProjection(): Observable<FireProjection> {
    return this.get<FireProjection>('fire');
  }
}
