import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {ListItemRowComponent} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {CostOfInvestingStore} from '../../store/cost-of-investing/cost-of-investing.store';

/**
 * One row on the growth card: what investing cost this year (commissions, fees, FX conversion, withholding
 * tax) and its share of returns before costs. A view over rows already collected. Renders nothing while loading,
 * on error, or when nothing was paid, so a book with no costs shows no row rather than a zero.
 */
@Component({
  selector: 'fns-cost-of-investing',
  imports: [ListItemRowComponent, MoneyPipe],
  templateUrl: './cost-of-investing.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [CostOfInvestingStore],
})
export class CostOfInvestingComponent {
  public readonly store = inject(CostOfInvestingStore);
}
