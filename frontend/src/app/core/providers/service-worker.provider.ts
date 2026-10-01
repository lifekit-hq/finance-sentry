import {type EnvironmentProviders, isDevMode, makeEnvironmentProviders} from '@angular/core';
import {provideServiceWorker} from '@angular/service-worker';

const SERVICE_WORKER_SCRIPT = 'ngsw-worker.js';
const REGISTRATION_DELAY_MS = 30_000;

export function provideAppServiceWorker(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideServiceWorker(SERVICE_WORKER_SCRIPT, {
      enabled: !isDevMode(),
      registrationStrategy: `registerWhenStable:${REGISTRATION_DELAY_MS}`,
    }),
  ]);
}
