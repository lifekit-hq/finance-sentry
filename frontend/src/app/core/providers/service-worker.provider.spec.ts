import {TestBed} from '@angular/core/testing';
import {SwRegistrationOptions} from '@angular/service-worker';

import {provideAppServiceWorker} from './service-worker.provider';

describe('provideAppServiceWorker', () => {
  it('registers the worker only once the app is stable, so first paint is never delayed', () => {
    TestBed.configureTestingModule({providers: [provideAppServiceWorker()]});

    const options = TestBed.inject(SwRegistrationOptions);

    expect(options.registrationStrategy).toMatch(/^registerWhenStable:\d+$/);
  });

  it('is disabled in dev mode so ng serve never serves a stale shell', () => {
    TestBed.configureTestingModule({providers: [provideAppServiceWorker()]});

    expect(TestBed.inject(SwRegistrationOptions).enabled).toBe(false);
  });
});
