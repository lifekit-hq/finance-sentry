import {ChangeDetectionStrategy, Component, input, linkedSignal, output} from '@angular/core';
import {ButtonComponent, ChipComponent} from '@lifekit-hq/ui';

import {
  ALERT_READ_STATE_FILTERS,
  ALERT_SEVERITY_FILTERS,
} from '../../constants/alert/alert-filter.constants';
import {type AlertFilter} from '../../models/alert/alert.model';

/**
 * Body of the alerts filter sheet (read state / severity), rendered by `cmn-filter-bar`. The page
 * filters on one value at a time, so choosing in either group replaces the draft; "Show results"
 * emits it and closing the sheet any other way drops it.
 */
@Component({
  selector: 'fns-alert-filter-sheet',
  imports: [ButtonComponent, ChipComponent],
  templateUrl: './alert-filter-sheet.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AlertFilterSheetComponent {
  public readonly initial = input.required<AlertFilter>();
  public readonly applied = output<AlertFilter>();

  public readonly readStates = ALERT_READ_STATE_FILTERS;
  public readonly severities = ALERT_SEVERITY_FILTERS;
  public readonly draft = linkedSignal<AlertFilter>(() => this.initial());

  public select(filter: AlertFilter): void {
    this.draft.set(filter);
  }

  public reset(): void {
    this.draft.set('all');
  }

  public apply(): void {
    this.applied.emit(this.draft());
  }
}
