import {inject, Injectable} from '@angular/core';
import {CmnDrawerService} from '@lifekit-hq/ui';
import {type Observable} from 'rxjs';

import {
  type DecisionSheetData,
  type RecommendationDto,
  type RecommendationVerdict,
} from '../../../shared/models/recommendation/recommendation.model';
import {DecisionSheetComponent} from '../components/decision-sheet/decision-sheet.component';
import {DECISION_SHEET_WIDTH} from '../constants/recommendation/recommendation.constants';

const SHEET_TITLE: Readonly<Record<RecommendationVerdict, string>> = {
  confirm: 'Confirm decision',
  reject: 'Reject proposal',
};

/** The one way into the Confirm and Reject sheets: a side panel from 768px, a bottom sheet below. */
@Injectable({providedIn: 'root'})
export class DecisionSheetService {
  private readonly drawer = inject(CmnDrawerService);

  /** Emits the decided proposal once recorded, or undefined when the sheet is dismissed. */
  public open(
    recommendation: RecommendationDto,
    verdict: RecommendationVerdict
  ): Observable<RecommendationDto | undefined> {
    return this.drawer
      .open<RecommendationDto, DecisionSheetData>(DecisionSheetComponent, {
        title: SHEET_TITLE[verdict],
        width: DECISION_SHEET_WIDTH,
        data: {recommendation, verdict},
      })
      .afterClosed();
  }
}
