import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ErrorMessageService} from '@lifekit-hq/core';
import {PushSubscriptionService} from '@lifekit-hq/core/pwa';
import {beforeEach, describe, expect, it} from 'vitest';

import {type PushDevice} from '../../models/push/push.model';
import {pushComputed} from './push.computed';
import {type PushAction} from './push.state';

const device: PushDevice = {
  id: 'd-1',
  deviceLabel: 'Phone',
  createdAt: '2026-10-01T00:00:00Z',
  lastSuccessAt: null,
  disabled: false,
};

describe('pushComputed', () => {
  const browser = {
    isSupported: signal(true),
    requiresInstall: signal(false),
    permission: signal<'default' | 'granted' | 'denied'>('default'),
  };
  const state = {
    status: signal<AsyncStatus>('idle'),
    errorCode: signal<Nullable<string>>(null),
    available: signal(true),
    pushEnabled: signal(false),
    devices: signal<PushDevice[]>([]),
    thisDeviceId: signal<Nullable<string>>(null),
    action: signal<PushAction>('idle'),
    actionErrorCode: signal<Nullable<string>>(null),
  };

  function computed() {
    return TestBed.runInInjectionContext(() => pushComputed(state));
  }

  beforeEach(() => {
    browser.isSupported.set(true);
    browser.requiresInstall.set(false);
    browser.permission.set('default');
    state.status.set('idle');
    state.errorCode.set(null);
    state.available.set(true);
    state.pushEnabled.set(false);
    state.devices.set([]);
    state.thisDeviceId.set(null);
    state.action.set('idle');
    state.actionErrorCode.set(null);
    TestBed.configureTestingModule({
      providers: [
        {provide: PushSubscriptionService, useValue: browser},
        {
          provide: ErrorMessageService,
          useValue: {resolve: (c: Nullable<string>) => (c === 'KNOWN' ? 'Known message' : null)},
        },
      ],
    });
  });

  it('marks this device in the rows', () => {
    state.devices.set([device, {...device, id: 'd-2'}]);
    state.thisDeviceId.set('d-2');

    expect(
      computed()
        .rows()
        .map(r => r.isThisDevice)
    ).toEqual([false, true]);
  });

  it('allows the toggle when this browser can subscribe', () => {
    expect(computed().canToggle()).toBe(true);
  });

  it.each([
    ['the server has push off', () => state.available.set(false)],
    ['the browser cannot do push', () => browser.isSupported.set(false)],
    ['iOS needs the app installed', () => browser.requiresInstall.set(true)],
    ['permission is blocked', () => browser.permission.set('denied')],
    ['an action is running', () => state.action.set('saving')],
  ])('blocks the toggle when %s and no device is registered', (_label, arrange) => {
    arrange();

    expect(computed().canToggle()).toBe(false);
  });

  it('still allows turning push off when this device is registered but permission was later blocked', () => {
    state.thisDeviceId.set('d-1');
    browser.permission.set('denied');

    expect(computed().canToggle()).toBe(true);
  });

  it.each([
    ['the server has push off', () => state.available.set(false)],
    ['the browser cannot do push', () => browser.isSupported.set(false)],
    ['iOS needs the app installed', () => browser.requiresInstall.set(true)],
    ['permission is blocked', () => browser.permission.set('denied')],
  ])('still allows turning push off when %s and no device is registered', (_label, arrange) => {
    state.pushEnabled.set(true);
    arrange();

    expect(computed().canToggle()).toBe(true);
  });

  it('blocks the toggle while an action is running even when push is on', () => {
    state.pushEnabled.set(true);
    state.action.set('saving');

    expect(computed().canToggle()).toBe(false);
  });

  it('explains why the browser cannot subscribe', () => {
    const c = computed();
    expect(c.browserNotice()).toBe('');

    browser.requiresInstall.set(true);
    expect(c.browserNotice()).toContain('Home Screen');

    browser.requiresInstall.set(false);
    browser.isSupported.set(false);
    expect(c.browserNotice()).toContain('cannot receive push');

    browser.isSupported.set(true);
    browser.permission.set('denied');
    expect(c.browserNotice()).toContain('blocked');
  });

  it('shows the server notice only once loaded and unavailable', () => {
    state.available.set(false);
    state.status.set('loading');
    const c = computed();
    expect(c.serverNotice()).toBe('');

    state.status.set('idle');
    expect(c.serverNotice()).toContain('not set up');
    expect(c.browserNotice()).toBe('');
  });

  it('resolves error codes with a fallback', () => {
    state.status.set('error');
    state.errorCode.set('KNOWN');
    state.actionErrorCode.set('UNKNOWN');
    const c = computed();

    expect(c.loadErrorMessage()).toBe('Known message');
    expect(c.actionErrorMessage()).toBe('Could not update push notifications.');

    state.errorCode.set(null);
    expect(c.loadErrorMessage()).toBe('Failed to load push settings.');
  });

  it('reports loading only while there is nothing to show yet', () => {
    state.status.set('loading');
    const c = computed();
    expect(c.isLoading()).toBe(true);

    state.devices.set([device]);
    expect(c.isLoading()).toBe(false);
  });
});
