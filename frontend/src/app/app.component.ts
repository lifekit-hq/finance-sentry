import {ChangeDetectionStrategy, Component} from '@angular/core';
import {RouterOutlet} from '@angular/router';

@Component({
  selector: 'fns-root',
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  host: {class: 'block h-full'},
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent {}
