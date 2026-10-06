import {TestBed} from '@angular/core/testing';
import {PushSubscriptionService} from '@lifekit-hq/core/pwa';
import {firstValueFrom, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {PUSH_DEVICE_STORAGE_KEY} from '../constants/push/push.constants';
import {PushNotificationsService} from './push-notifications.service';
import {PushSessionService} from './push-session.service';

describe('PushSessionService', () => {
  let api: {remove: ReturnType<typeof vi.fn>};
  let browser: {unsubscribe: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    localStorage.clear();
    api = {remove: vi.fn().mockReturnValue(of(undefined))};
    browser = {unsubscribe: vi.fn().mockResolvedValue(undefined)};
    TestBed.configureTestingModule({
      providers: [
        {provide: PushNotificationsService, useValue: api},
        {provide: PushSubscriptionService, useValue: browser},
      ],
    });
  });

  it('deletes the server subscription, drops the browser one and forgets the id', async () => {
    localStorage.setItem(PUSH_DEVICE_STORAGE_KEY, 'd-1');

    await firstValueFrom(TestBed.inject(PushSessionService).release(), {defaultValue: undefined});

    expect(api.remove).toHaveBeenCalledWith('d-1');
    expect(browser.unsubscribe).toHaveBeenCalled();
    expect(localStorage.getItem(PUSH_DEVICE_STORAGE_KEY)).toBeNull();
  });

  it('skips the API call when this browser never registered', async () => {
    await firstValueFrom(TestBed.inject(PushSessionService).release(), {defaultValue: undefined});

    expect(api.remove).not.toHaveBeenCalled();
    expect(browser.unsubscribe).toHaveBeenCalled();
  });

  it('still drops the browser subscription when the API call fails', async () => {
    localStorage.setItem(PUSH_DEVICE_STORAGE_KEY, 'd-1');
    api.remove.mockReturnValue(throwError(() => new Error('offline')));

    await firstValueFrom(TestBed.inject(PushSessionService).release(), {defaultValue: undefined});

    expect(browser.unsubscribe).toHaveBeenCalled();
  });

  it('never errors, even if the browser refuses to unsubscribe', async () => {
    browser.unsubscribe.mockRejectedValue(new Error('nope'));

    await expect(
      firstValueFrom(TestBed.inject(PushSessionService).release(), {defaultValue: undefined})
    ).resolves.toBeUndefined();
  });
});
