import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {PushSubscriptionService} from '@lifekit-hq/core/pwa';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type PushDevice} from '../../models/push/push.model';
import {PushNotificationsService} from '../../services/push-notifications.service';
import {PushStore} from './push.store';

const DEVICE: PushDevice = {
  id: 'd-1',
  deviceLabel: 'Phone',
  createdAt: '2026-10-01T00:00:00Z',
  lastSuccessAt: null,
  disabled: false,
};

describe('PushStore', () => {
  let service: Record<'getPublicKey' | 'listDevices' | 'getPreferences', ReturnType<typeof vi.fn>>;

  function inject(): InstanceType<typeof PushStore> {
    TestBed.configureTestingModule({
      providers: [
        PushStore,
        {provide: PushNotificationsService, useValue: service},
        {
          provide: PushSubscriptionService,
          useValue: {
            isSupported: signal(true),
            requiresInstall: signal(false),
            permission: signal('default'),
          },
        },
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(PushStore);
  }

  beforeEach(() => {
    localStorage.clear();
    service = {
      getPublicKey: vi.fn().mockReturnValue(of({available: true, publicKey: 'vapid'})),
      listDevices: vi.fn().mockReturnValue(of([DEVICE])),
      getPreferences: vi.fn().mockReturnValue(of({pushEnabled: true})),
    };
  });

  it('is loading until the answers arrive', () => {
    service.listDevices.mockReturnValue(NEVER);

    const store = inject();

    expect(store.status()).toBe('loading');
    expect(store.isLoading()).toBe(true);
  });

  it('settles on success with the loaded devices and no error', () => {
    const store = inject();

    expect(store.status()).toBe('success');
    expect(store.devices()).toEqual([DEVICE]);
    expect(store.errorMessage()).toBe('');
    expect(store.serverNotice()).toBe('');
  });

  it('tells the person when the server has push switched off', () => {
    service.getPublicKey.mockReturnValue(of({available: false, publicKey: null}));

    expect(inject().serverNotice()).toContain('not set up');
  });

  it('resolves a known load error code through the registry', () => {
    service.listDevices.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'FORBIDDEN'}}))
    );

    const store = inject();

    expect(store.hasError()).toBe(true);
    expect(store.errorMessage()).toBe(ERROR_MESSAGES_REGISTRY['FORBIDDEN']);
  });

  it('falls back to the push default for an unknown load error', () => {
    service.listDevices.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));

    expect(inject().errorMessage()).toBe('Failed to load push settings.');
  });
});
