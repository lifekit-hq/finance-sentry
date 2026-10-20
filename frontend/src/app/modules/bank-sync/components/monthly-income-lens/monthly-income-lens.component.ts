import {ChangeDetectionStrategy, Component, inject} from '@angular/core';

import {MonthlyIncomeLensStore} from '../../store/monthly-income-lens/monthly-income-lens.store';

/**
 * The hero's monthly-income lens (P5): the net worth also shown as the monthly income it could
 * pay at the shared safe withdrawal rate, with that assumption in words right under it (C8).
 */
@Component({
  selector: 'fns-monthly-income-lens',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block empty:hidden'},
  providers: [MonthlyIncomeLensStore],
  templateUrl: './monthly-income-lens.component.html',
})
export class MonthlyIncomeLensComponent {
  public readonly store = inject(MonthlyIncomeLensStore);
}
