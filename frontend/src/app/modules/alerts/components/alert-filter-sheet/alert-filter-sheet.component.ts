import {ChangeDetectionStrategy, Component, inject, signal} from '@angular/core';
import {ButtonComponent, ChipComponent, CMN_DRAWER_DATA, CmnDrawerRef} from '@lifekit-hq/ui';

import {
  ALERT_READ_STATE_FILTERS,
  ALERT_SEVERITY_FILTERS,
} from '../../constants/alert/alert-filter.constants';
import {type AlertFilter} from '../../models/alert/alert.model';

/**
 * Body of the alerts filter sheet (read state / severity). The page filters on one value at a
 * time, so choosing in either group replaces the draft; "Show results" hands it back and closing
 * the sheet any other way drops it.
 */
@Component({
  selector: 'fns-alert-filter-sheet',
  imports: [ButtonComponent, ChipComponent],
  templateUrl: './alert-filter-sheet.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AlertFilterSheetComponent {
  private readonly drawerRef = inject<CmnDrawerRef<AlertFilter>>(CmnDrawerRef);

  public readonly readStates = ALERT_READ_STATE_FILTERS;
  public readonly severities = ALERT_SEVERITY_FILTERS;
  public readonly draft = signal<AlertFilter>(inject<AlertFilter>(CMN_DRAWER_DATA));

  public select(filter: AlertFilter): void {
    this.draft.set(filter);
  }

  public reset(): void {
    this.draft.set('all');
  }

  public apply(): void {
    this.drawerRef.close(this.draft());
  }
}
