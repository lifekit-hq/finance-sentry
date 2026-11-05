import {DatePipe, DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {CardComponent, LineChartComponent, ToggleComponent} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {FxImpactStore} from '../../store/fx-impact/fx-impact.store';
import {RATE_FORMAT} from './fx-impact-widget.constants';

/**
 * What exchange-rate movement did to plans billed in a currency other than USD, EUR or the base currency: the
 * payment is fixed in its own currency, so a weaker one makes it cheaper to carry. Data-gated: it renders nothing
 * (and offers no toggle) unless such a plan exists.
 */
@Component({
  selector: 'fns-fx-impact-widget',
  imports: [CardComponent, DatePipe, DecimalPipe, LineChartComponent, MoneyPipe, ToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [FxImpactStore],
  host: {'[hidden]': '!store.available()'},
  templateUrl: './fx-impact-widget.component.html',
})
export class FxImpactWidgetComponent {
  public readonly store = inject(FxImpactStore);
  public readonly rateFormat = RATE_FORMAT;
}
