import {TestBed} from '@angular/core/testing';
import {patchState, signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type PushDevice} from '../../models/push/push.model';
import {pushMethods} from './push.methods';
import {initialPushState} from './push.state';

const phone: PushDevice = {
  id: 'd-1',
  deviceLabel: 'Chrome on Android',
  createdAt: '2026-10-01T00:00:00Z',
  lastSuccessAt: null,
  disabled: false,
};
const laptop: PushDevice = {...phone, id: 'd-2', deviceLabel: 'Firefox on Linux'};

function build() {
  const state = signalState(initialPushState);
  return {state, methods: pushMethods(state as never)};
}

describe('pushMethods', () => {
  it('setLoaded keeps the stored device id only when the server still lists it', () => {
    const {state, methods} = build();

    methods.setLoaded({available: true, publicKey: 'k'}, [phone], {pushEnabled: true}, 'd-1');
    expect(state.thisDeviceId()).toBe('d-1');
    expect(state.publicKey()).toBe('k');
    expect(state.pushEnabled()).toBe(true);

    methods.setLoaded({available: true, publicKey: 'k'}, [laptop], {pushEnabled: true}, 'd-1');
    expect(state.thisDeviceId()).toBeNull();
  });

  it('setEnabled adds the device, marks it as this one and turns push on', () => {
    const {state, methods} = build();
    TestBed.runInInjectionContext(() => patchState(state as never, {devices: [laptop]}));

    methods.setEnabled(phone);

    expect(
      state
        .devices()
        .map(d => d.id)
        .sort()
    ).toEqual(['d-1', 'd-2']);
    expect(state.thisDeviceId()).toBe('d-1');
    expect(state.pushEnabled()).toBe(true);
    expect(state.action()).toBe('idle');
  });

  it('setEnabled replaces a device that re-registered under the same id', () => {
    const {state, methods} = build();
    methods.setEnabled(phone);
    methods.setEnabled({...phone, deviceLabel: 'Renamed'});

    expect(state.devices()).toHaveLength(1);
    expect(state.devices()[0].deviceLabel).toBe('Renamed');
  });

  it('setRemoved drops the device and forgets this device when it was the one removed', () => {
    const {state, methods} = build();
    methods.setEnabled(phone);
    methods.setAction('removing', 'd-1');

    methods.setRemoved('d-1');

    expect(state.devices()).toEqual([]);
    expect(state.thisDeviceId()).toBeNull();
    expect(state.removingId()).toBeNull();
    expect(state.action()).toBe('idle');
  });

  it('setRemoved leaves this device alone when another one is removed', () => {
    const {state, methods} = build();
    methods.setEnabled(phone);
    methods.setEnabled(laptop);
    methods.setRemoved('d-1');

    expect(state.thisDeviceId()).toBe('d-2');
  });

  it('setActionError ends the action and records the code', () => {
    const {state, methods} = build();
    methods.setAction('enabling');
    methods.setActionError('PUSH_UNAVAILABLE');

    expect(state.action()).toBe('idle');
    expect(state.actionErrorCode()).toBe('PUSH_UNAVAILABLE');
  });

  it('setLoadError and setLoading drive the load status', () => {
    const {state, methods} = build();
    methods.setLoading();
    expect(state.status()).toBe('loading');
    methods.setLoadError('X');
    expect(state.status()).toBe('error');
    expect(state.errorCode()).toBe('X');
  });

  it('setPreference stores the saved value', () => {
    const {state, methods} = build();
    methods.setAction('saving');
    methods.setPreference(true);
    expect(state.pushEnabled()).toBe(true);
    expect(state.action()).toBe('idle');
  });
});
