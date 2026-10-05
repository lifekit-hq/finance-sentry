import {ChangeDetectionStrategy, Component} from '@angular/core';
import {AlertComponent} from '@lifekit-hq/ui';

import {FLEX_DELIVERY_SETTINGS, FLEX_QUERY_SECTIONS} from './ibkr-flex-guide.constants';

@Component({
  selector: 'fns-ibkr-flex-guide',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AlertComponent],
  templateUrl: './ibkr-flex-guide.component.html',
})
export class IbkrFlexGuideComponent {
  public readonly sections = FLEX_QUERY_SECTIONS;
  public readonly deliverySettings = FLEX_DELIVERY_SETTINGS;
}
