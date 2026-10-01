// Registers the shared lk-* PWA elements before bootstrap so lk-install-hint captures the
// browser's one-shot `beforeinstallprompt` event.
import '@lifekit-hq/elements';

import {bootstrapApplication} from '@angular/platform-browser';

import {AppComponent} from './app/app.component';
import {appConfig} from './app/app.config';

bootstrapApplication(AppComponent, appConfig).catch(err => console.error(err));
