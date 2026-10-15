import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';
import {CMN_DRAWER_DATA} from '@lifekit-hq/ui';

import {
  HEADLINE_BADGE_SUMMARY,
  HEADLINE_REASON_FALLBACK,
} from '../../constants/headline/headline.constants';
import {
  type HeadlineReason,
  type HeadlineReasonSheetData,
} from '../../models/headline/headline.model';
import {HeadlineUtils} from '../../utils/headline.utils';

interface ReasonRow {
  code: string;
  text: string;
  sourceLabel: Nullable<string>;
}

/**
 * The body of the reason sheet a quality badge opens: each typed reason in plain words from the
 * error-message registry (a code with no entry gets one generic line), then when the figure was
 * last measured and how much of it is covered.
 */
@Component({
  selector: 'fns-headline-reason-sheet',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './headline-reason-sheet.component.html',
})
export class HeadlineReasonSheetComponent {
  private readonly messages = inject(ErrorMessageService);
  private readonly data = inject<HeadlineReasonSheetData>(CMN_DRAWER_DATA);

  public readonly headline = this.data.headline;
  public readonly summary = HEADLINE_BADGE_SUMMARY[this.data.badge.kind];
  public readonly reasons: ReasonRow[] = this.data.headline.reasons.map(r => this.row(r));
  public readonly coverage: Nullable<string> =
    this.data.headline.coverage == null
      ? null
      : HeadlineUtils.coverageLabel(this.data.headline.coverage);

  private row(reason: HeadlineReason): ReasonRow {
    return {
      code: reason.code,
      text: this.messages.resolve(reason.code) ?? HEADLINE_REASON_FALLBACK,
      sourceLabel: reason.source ? HeadlineUtils.sourceLabel(reason.source) : null,
    };
  }
}
