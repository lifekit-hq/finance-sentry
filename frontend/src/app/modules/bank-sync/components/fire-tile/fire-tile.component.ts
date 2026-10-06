import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {RouterLink} from '@angular/router';
import {AlertComponent, CardComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {FireStore} from '../../store/fire/fire.store';

@Component({
  selector: 'fns-fire-tile',
  imports: [AlertComponent, CardComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [FireStore],
  templateUrl: './fire-tile.component.html',
})
export class FireTileComponent {
  public readonly store = inject(FireStore);
  public readonly settingsRoute = AppRoute.Settings;
}
