import {ChangeDetectionStrategy, Component, computed, inject, input} from '@angular/core';

import {type HeadlineBadgeKind, type MaybeHeadline} from '../../models/headline/headline.model';
import {HeadlineUtils} from '../../utils/headline.utils';
import {HeadlineReasonSheetService} from './headline-reason-sheet.service';

const KIND_CLASSES: Readonly<Record<HeadlineBadgeKind, string>> = {
  estimated: 'bg-status-info-subtle text-status-info',
  stale: 'bg-status-warning-subtle text-status-warning',
  partial: 'bg-status-warning-subtle text-status-warning',
  unavailable: 'bg-status-error-subtle text-status-error',
};

/**
 * The quality badge on a headline figure: nothing for a complete figure (or no headline at all),
 * else "Estimated", "Stale", "Partial" or "Unavailable". The whole control is a 44 x 44 px
 * button (negative vertical margins keep it from growing the row it sits in) that opens the
 * reason sheet: the reason is one tap away. A page shows this in place of any ad-hoc stale or
 * approximate hint, never beside one.
 */
@Component({
  selector: 'fns-headline-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './headline-badge.component.html',
  host: {class: 'inline-flex'},
})
export class HeadlineBadgeComponent {
  private readonly sheet = inject(HeadlineReasonSheetService);

  public readonly headline = input<MaybeHeadline>(null);
  /** What the figure is called ("Net worth"): the sheet's title and the button's accessible name. */
  public readonly label = input('This figure');

  public readonly badge = computed(() => HeadlineUtils.badge(this.headline()));
  public readonly classes = computed(() => {
    const badge = this.badge();
    return badge ? KIND_CLASSES[badge.kind] : '';
  });
  public readonly ariaLabel = computed(
    () => `${this.label()}: ${this.badge()?.label.toLowerCase() ?? ''}. Show why`
  );

  public open(): void {
    const headline = this.headline();
    const badge = this.badge();
    if (headline && badge) {
      this.sheet.open(headline, badge, this.label());
    }
  }
}
