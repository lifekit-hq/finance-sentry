import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {FUTURE_RAISE} from './career-events.fixtures';
import {careerEventsMethods} from './career-events.methods';
import {initialCareerEventsState} from './career-events.state';

function build() {
  const state = signalState(initialCareerEventsState);
  return {state, methods: careerEventsMethods(state)};
}

describe('careerEventsMethods', () => {
  it('stores the events', () => {
    const {state, methods} = build();

    methods.setEvents([FUTURE_RAISE]);

    expect(state.events()).toEqual([FUTURE_RAISE]);
  });

  it('marks an event busy and clears an earlier error', () => {
    const {state, methods} = build();
    methods.setActionError('X');

    methods.setBusy('e-1');

    expect(state.busyId()).toBe('e-1');
    expect(state.actionErrorCode()).toBeNull();
  });

  it('goes idle when the action completes', () => {
    const {state, methods} = build();
    methods.setBusy('e-1');

    methods.setIdle();

    expect(state.busyId()).toBeNull();
  });

  it('records an action error and stops being busy', () => {
    const {state, methods} = build();
    methods.setBusy('e-1');

    methods.setActionError('CAREER_EVENT_NOT_FOUND');

    expect(state.busyId()).toBeNull();
    expect(state.actionErrorCode()).toBe('CAREER_EVENT_NOT_FOUND');
  });

  it('clears the action error alone', () => {
    const {state, methods} = build();
    methods.setActionError('X');

    methods.clearActionError();

    expect(state.actionErrorCode()).toBeNull();
  });
});
