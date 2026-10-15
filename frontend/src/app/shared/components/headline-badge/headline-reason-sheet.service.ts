import {inject, Injectable} from '@angular/core';
import {CmnDrawerService} from '@lifekit-hq/ui';

import {HEADLINE_REASON_SHEET_WIDTH} from '../../constants/headline/headline.constants';
import {type Headline, type HeadlineBadge} from '../../models/headline/headline.model';
import {HeadlineReasonSheetComponent} from '../headline-reason-sheet/headline-reason-sheet.component';

/** Opens the reason sheet: a side panel from 768px, a bottom sheet below. */
@Injectable({providedIn: 'root'})
export class HeadlineReasonSheetService {
  private readonly drawer = inject(CmnDrawerService);

  public open(headline: Headline, badge: HeadlineBadge, title: string): void {
    this.drawer.open(HeadlineReasonSheetComponent, {
      title,
      width: HEADLINE_REASON_SHEET_WIDTH,
      data: {headline, badge},
    });
  }
}
