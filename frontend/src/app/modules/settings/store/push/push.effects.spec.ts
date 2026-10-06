import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {PushSubscriptionService} from '@lifekit-hq/core/pwa';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {PUSH_DEVICE_STORAGE_KEY} from '../../constants/push/push.constants';
import {type PushDevice} from '../../models/push/push.model';
import {PushNotificationsService} from '../../services/push-notifications.service';
import {pushEffects, pushHooks} from './push.effects';

const DEVICE: PushDevice = {
  id: 'd-1',
  deviceLabel: 'Phone',
  createdAt: '2026-10-01T00:00:00Z',
  lastSuccessAt: null,
  disabled: false,
};
const BROWSER_JSON = {endpoint: 'https://push.example/x', keys: {p256dh: 'p', auth: 'a'}};

function buildStore(
  overrides: {publicKey?: Nullable<string>; thisDeviceId?: Nullable<string>} = {}
) {
  return {
    publicKey: signal<Nullable<string>>(
      overrides.publicKey === undefined ? 'vapid' : overrides.publicKey
    ),
    thisDeviceId: signal<Nullable<string>>(overrides.thisDeviceId ?? null),
    setLoading: vi.fn(),
    setLoaded: vi.fn(),
    setLoadError: vi.fn(),
    setAction: vi.fn(),
    setActionError: vi.fn(),
    setEnabled: vi.fn(),
    setPreference: vi.fn(),
    setRemoved: vi.fn(),
  };
}

function apiError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 503, error: {errorCode}}));
}

describe('pushEffects', () => {
  let service: Record<
    'getPublicKey' | 'listDevices' | 'getPreferences' | 'register' | 'remove' | 'setPreferences',
    ReturnType<typeof vi.fn>
  >;
  let browser: {
    subscribe: ReturnType<typeof vi.fn>;
    unsubscribe: ReturnType<typeof vi.fn>;
    permission: ReturnType<typeof signal<string>>;
  };

  beforeEach(() => {
    localStorage.clear();
    service = {
      getPublicKey: vi.fn().mockReturnValue(of({available: true, publicKey: 'vapid'})),
      listDevices: vi.fn().mockReturnValue(of([DEVICE])),
      getPreferences: vi.fn().mockReturnValue(of({pushEnabled: true})),
      register: vi.fn().mockReturnValue(of(DEVICE)),
      remove: vi.fn().mockReturnValue(of(undefined)),
      setPreferences: vi.fn().mockImplementation((pushEnabled: boolean) => of({pushEnabled})),
    };
    browser = {
      subscribe: vi.fn().mockResolvedValue(BROWSER_JSON),
      unsubscribe: vi.fn().mockResolvedValue(undefined),
      permission: signal('granted'),
    };
    TestBed.configureTestingModule({
      providers: [
        {provide: PushNotificationsService, useValue: service},
        {provide: PushSubscriptionService, useValue: browser},
      ],
    });
  });

  function effects(store: ReturnType<typeof buildStore>) {
    return TestBed.runInInjectionContext(() => pushEffects(store));
  }

  describe('load', () => {
    it('loads the key, devices and preference together with the stored device id', () => {
      localStorage.setItem(PUSH_DEVICE_STORAGE_KEY, 'd-1');
      const store = buildStore();

      effects(store).load();

      expect(store.setLoading).toHaveBeenCalled();
      expect(store.setLoaded).toHaveBeenCalledWith(
        {available: true, publicKey: 'vapid'},
        [DEVICE],
        {pushEnabled: true},
        'd-1'
      );
    });

    it('reports the API error code', () => {
      service.listDevices.mockReturnValue(apiError('BOOM'));
      const store = buildStore();

      effects(store).load();

      expect(store.setLoadError).toHaveBeenCalledWith('BOOM');
    });

    it('hooks load on init', () => {
      const load = vi.fn();
      pushHooks({load});
      expect(load).toHaveBeenCalled();
    });
  });

  describe('enable', () => {
    it('subscribes with the server key, registers the device, turns push on and remembers the id', async () => {
      const store = buildStore();

      effects(store).enable();
      await vi.waitFor(() => expect(store.setEnabled).toHaveBeenCalledWith(DEVICE));

      expect(browser.subscribe).toHaveBeenCalledWith('vapid');
      expect(service.register).toHaveBeenCalledWith({
        endpoint: 'https://push.example/x',
        keys: {p256dh: 'p', auth: 'a'},
      });
      expect(service.setPreferences).toHaveBeenCalledWith(true);
      expect(localStorage.getItem(PUSH_DEVICE_STORAGE_KEY)).toBe('d-1');
    });

    it('starts the browser subscription synchronously, inside the click', () => {
      effects(buildStore()).enable();

      expect(browser.subscribe).toHaveBeenCalledTimes(1);
    });

    it('fails with PUSH_UNAVAILABLE when the server gave no key', () => {
      const store = buildStore({publicKey: null});

      effects(store).enable();

      expect(browser.subscribe).not.toHaveBeenCalled();
      expect(store.setActionError).toHaveBeenCalledWith('PUSH_UNAVAILABLE');
    });

    it('reports a blocked permission and leaves nothing behind', async () => {
      browser.subscribe.mockRejectedValue(new Error('denied'));
      browser.permission.set('denied');
      const store = buildStore();

      effects(store).enable();
      await vi.waitFor(() =>
        expect(store.setActionError).toHaveBeenCalledWith('PUSH_PERMISSION_DENIED')
      );

      expect(service.register).not.toHaveBeenCalled();
      expect(localStorage.getItem(PUSH_DEVICE_STORAGE_KEY)).toBeNull();
    });

    it('reports a generic browser failure', async () => {
      browser.subscribe.mockRejectedValue(new Error('no push service'));
      const store = buildStore();

      effects(store).enable();
      await vi.waitFor(() =>
        expect(store.setActionError).toHaveBeenCalledWith('PUSH_SUBSCRIBE_FAILED')
      );
    });

    it('drops the browser subscription when the server refuses it, and reports the API code', async () => {
      service.register.mockReturnValue(apiError('PUSH_SUBSCRIPTION_INVALID'));
      const store = buildStore();

      effects(store).enable();
      await vi.waitFor(() =>
        expect(store.setActionError).toHaveBeenCalledWith('PUSH_SUBSCRIPTION_INVALID')
      );

      expect(browser.unsubscribe).toHaveBeenCalled();
      expect(store.setEnabled).not.toHaveBeenCalled();
      expect(localStorage.getItem(PUSH_DEVICE_STORAGE_KEY)).toBeNull();
    });

    it('rejects an incomplete browser subscription', async () => {
      browser.subscribe.mockResolvedValue({endpoint: 'https://push.example/x'});
      const store = buildStore();

      effects(store).enable();
      await vi.waitFor(() =>
        expect(store.setActionError).toHaveBeenCalledWith('PUSH_SUBSCRIBE_FAILED')
      );

      expect(service.register).not.toHaveBeenCalled();
    });
  });

  describe('togglePush', () => {
    it('turning on without a registered device registers this browser', () => {
      const store = buildStore();

      effects(store).togglePush(true);

      expect(browser.subscribe).toHaveBeenCalled();
    });

    it('turning on with a registered device only saves the preference', () => {
      const store = buildStore({thisDeviceId: 'd-1'});

      effects(store).togglePush(true);

      expect(browser.subscribe).not.toHaveBeenCalled();
      expect(service.setPreferences).toHaveBeenCalledWith(true);
      expect(store.setPreference).toHaveBeenCalledWith(true);
    });

    it('turning off saves the preference and keeps the devices', () => {
      const store = buildStore({thisDeviceId: 'd-1'});

      effects(store).togglePush(false);

      expect(service.setPreferences).toHaveBeenCalledWith(false);
      expect(store.setPreference).toHaveBeenCalledWith(false);
      expect(service.remove).not.toHaveBeenCalled();
    });

    it('reports a failed save', () => {
      service.setPreferences.mockReturnValue(apiError('PUSH_UNAVAILABLE'));
      const store = buildStore({thisDeviceId: 'd-1'});

      effects(store).togglePush(true);

      expect(store.setActionError).toHaveBeenCalledWith('PUSH_UNAVAILABLE');
    });
  });

  describe('remove', () => {
    it('removing another device only calls the API', async () => {
      const store = buildStore({thisDeviceId: 'd-1'});

      effects(store).remove('d-2');

      await vi.waitFor(() => expect(store.setRemoved).toHaveBeenCalledWith('d-2'));
      expect(service.remove).toHaveBeenCalledWith('d-2');
      expect(browser.unsubscribe).not.toHaveBeenCalled();
    });

    it('removing this device also drops the browser subscription and the stored id', async () => {
      localStorage.setItem(PUSH_DEVICE_STORAGE_KEY, 'd-1');
      const store = buildStore({thisDeviceId: 'd-1'});

      effects(store).remove('d-1');

      await vi.waitFor(() => expect(store.setRemoved).toHaveBeenCalledWith('d-1'));
      expect(browser.unsubscribe).toHaveBeenCalled();
      expect(localStorage.getItem(PUSH_DEVICE_STORAGE_KEY)).toBeNull();
    });

    it('reports a failed removal and keeps the device', () => {
      service.remove.mockReturnValue(apiError('PUSH_SUBSCRIPTION_NOT_FOUND'));
      const store = buildStore({thisDeviceId: 'd-1'});

      effects(store).remove('d-1');

      expect(store.setActionError).toHaveBeenCalledWith('PUSH_SUBSCRIPTION_NOT_FOUND');
      expect(store.setRemoved).not.toHaveBeenCalled();
    });
  });
});
