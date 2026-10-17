import {provideHttpClient, withInterceptors, withXhr} from '@angular/common/http';
import {type ApplicationConfig} from '@angular/core';
import {provideRouter, withPreloading} from '@angular/router';
import {provideApiBaseUrl, TabPreloadStrategy, withLkViewTransitions} from '@lifekit-hq/core';

import {environment} from '../environments/environment';
import {APP_ROUTES} from './app.routes';
import {provideAppIcons} from './core/providers/app-icons.provider';
import {provideAppInit} from './core/providers/app-init.provider';
import {provideDecimalPipe} from './core/providers/decimal-pipe.provider';
import {provideErrorHandler} from './core/providers/error-handler.provider';
import {provideErrorMessages} from './core/providers/error-messages.provider';
import {provideAppLocale} from './core/providers/locale.provider';
import {provideAppPreferences} from './core/providers/preferences.provider';
import {provideAppServiceWorker} from './core/providers/service-worker.provider';
import {provideTitleStrategy} from './core/providers/title-strategy.provider';
import {authInterceptor} from './modules/auth/interceptors/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    // Bottom tabs preload on idle (skipped under Save-Data or metered). The first load is not
    // crossfaded: the fade would hold the page's first taps and screenshots behind the snapshot.
    provideRouter(
      APP_ROUTES,
      withPreloading(TabPreloadStrategy),
      withLkViewTransitions({skipInitialTransition: true})
    ),
    provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
    provideApiBaseUrl(environment.apiBaseUrl),
    provideErrorHandler(),
    provideErrorMessages(),
    provideAppLocale(),
    provideAppIcons(),
    provideAppPreferences(),
    provideAppInit(),
    provideDecimalPipe(),
    provideAppServiceWorker(),
    provideTitleStrategy(),
  ],
};
