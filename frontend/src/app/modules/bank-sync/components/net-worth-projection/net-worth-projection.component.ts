import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {SkeletonComponent} from '@lifekit-hq/ui';

import {NetWorthProjectionStore} from '../../store/net-worth-projection/net-worth-projection.store';

/**
 * The hero's twelve-month projection (M10): the central figure, its range across the shared
 * return assumptions, and those assumptions in words right under it (C8).
 */
@Component({
  selector: 'fns-net-worth-projection',
  imports: [SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  providers: [NetWorthProjectionStore],
  templateUrl: './net-worth-projection.component.html',
})
export class NetWorthProjectionComponent {
  public readonly store = inject(NetWorthProjectionStore);
}
