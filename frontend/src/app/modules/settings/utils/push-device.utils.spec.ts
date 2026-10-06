import {afterEach, describe, expect, it, vi} from 'vitest';

import {PUSH_DEVICE_STORAGE_KEY} from '../constants/push/push.constants';
import {PushDeviceUtils} from './push-device.utils';

describe('PushDeviceUtils', () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('round-trips the device id through storage', () => {
    PushDeviceUtils.writeId('d-1');

    expect(localStorage.getItem(PUSH_DEVICE_STORAGE_KEY)).toBe('d-1');
    expect(PushDeviceUtils.readId()).toBe('d-1');
  });

  it('forgets the device id', () => {
    PushDeviceUtils.writeId('d-1');
    PushDeviceUtils.clearId();

    expect(PushDeviceUtils.readId()).toBeNull();
  });

  it('reads null when nothing is stored', () => {
    expect(PushDeviceUtils.readId()).toBeNull();
  });

  it('survives storage that throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(PushDeviceUtils.readId()).toBeNull();
    expect(() => PushDeviceUtils.writeId('d-1')).not.toThrow();
    expect(() => PushDeviceUtils.clearId()).not.toThrow();
  });

  it('builds the API request from the browser JSON', () => {
    expect(
      PushDeviceUtils.toRequest({
        endpoint: 'https://push.example/x',
        keys: {p256dh: 'p', auth: 'a'},
      })
    ).toEqual({endpoint: 'https://push.example/x', keys: {p256dh: 'p', auth: 'a'}});
  });

  it.each<Record<string, unknown>>([
    {keys: {p256dh: 'p', auth: 'a'}},
    {endpoint: 'https://push.example/x'},
    {endpoint: 'https://push.example/x', keys: {p256dh: 'p'}},
    {endpoint: 'https://push.example/x', keys: {auth: 'a'}},
  ])('rejects an incomplete subscription %#', json => {
    expect(() => PushDeviceUtils.toRequest(json as PushSubscriptionJSON)).toThrow();
  });
});
