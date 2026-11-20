import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {AsyncStateComponent, ListItemRowComponent, SkeletonComponent} from '@lifekit-hq/ui';

import {RiskPictureStore} from '../../store/risk-picture/risk-picture.store';

/**
 * The risk picture (finance-framework M8): volatility, maximum drawdown against the owner's tolerance, beta against
 * SPY, the two largest-position weights against the stored position flag, and the crypto share. Every figure sits
 * above a one-line explanation and the basis it was measured on; a figure that cannot be measured says why instead
 * of showing a number. Nothing is computed here: the figures arrive measured and are only formatted.
 */
@Component({
  selector: 'fns-risk-picture',
  imports: [AsyncStateComponent, ListItemRowComponent, SkeletonComponent],
  templateUrl: './risk-picture.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [RiskPictureStore],
})
export class RiskPictureComponent {
  public readonly store = inject(RiskPictureStore);
}
