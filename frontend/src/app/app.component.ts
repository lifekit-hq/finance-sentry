import {ChangeDetectionStrategy, Component, CUSTOM_ELEMENTS_SCHEMA, inject} from '@angular/core';
import {RouterOutlet} from '@angular/router';
import {AppUpdateService} from '@lifekit-hq/core/pwa';

@Component({
  selector: 'fns-root',
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
})
export class AppComponent {
  public readonly appUpdate = inject(AppUpdateService);
}
