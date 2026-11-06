import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {MONTHLY_SALARY} from './income-sources.fixtures';
import {incomeSourcesMethods} from './income-sources.methods';
import {initialIncomeSourcesState} from './income-sources.state';

function build() {
  const state = signalState(initialIncomeSourcesState);
  return {state, methods: incomeSourcesMethods(state)};
}

describe('incomeSourcesMethods', () => {
  it('stores the sources', () => {
    const {state, methods} = build();

    methods.setSources([MONTHLY_SALARY]);

    expect(state.sources()).toEqual([MONTHLY_SALARY]);
  });

  it('marks a source busy and clears an earlier error', () => {
    const {state, methods} = build();
    methods.setActionError('X');

    methods.setBusy('s-1');

    expect(state.busyId()).toBe('s-1');
    expect(state.actionErrorCode()).toBeNull();
  });

  it('goes idle when the action completes', () => {
    const {state, methods} = build();
    methods.setBusy('s-1');

    methods.setIdle();

    expect(state.busyId()).toBeNull();
  });

  it('records an action error and stops being busy', () => {
    const {state, methods} = build();
    methods.setBusy('s-1');

    methods.setActionError('INCOME_SOURCE_NOT_FOUND');

    expect(state.busyId()).toBeNull();
    expect(state.actionErrorCode()).toBe('INCOME_SOURCE_NOT_FOUND');
  });

  it('clears the action error alone', () => {
    const {state, methods} = build();
    methods.setActionError('X');

    methods.clearActionError();

    expect(state.actionErrorCode()).toBeNull();
  });
});
