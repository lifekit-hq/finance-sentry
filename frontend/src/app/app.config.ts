import {provideHttpClient, withInterceptors, withXhr} from '@angular/common/http';
import {type ApplicationConfig} from '@angular/core';
import {provideRouter} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';

import {environment} from '../environments/environment';
import {APP_ROUTES} from './app.routes';
import {provideAppIcons} from './core/providers/app-icons.provider';
import {provideAppInit} from './core/providers/app-init.provider';
import {provideDecimalPipe} from './core/providers/decimal-pipe.provider';
import {provideErrorHandler} from './core/providers/error-handler.provider';
import {provideErrorMessages} from './core/providers/error-messages.provider';
import {provideAppServiceWorker} from './core/providers/service-worker.provider';
import {authInterceptor} from './modules/auth/interceptors/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(APP_ROUTES),
    provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
    provideApiBaseUrl(environment.apiBaseUrl),
    provideErrorHandler(),
    provideErrorMessages(),
    provideAppIcons(),
    provideAppInit(),
    provideDecimalPipe(),
    provideAppServiceWorker(),
  ],
};
