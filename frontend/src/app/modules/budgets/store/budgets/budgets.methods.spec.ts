import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {budgetsMethods} from './budgets.methods';
import {initialBudgetsState} from './budgets.state';

function setup() {
  const state = signalState(initialBudgetsState);
  return {state, methods: budgetsMethods(state)};
}

describe('budgetsMethods refresh failure', () => {
  it('flags a failed refresh without touching the list', () => {
    const {state, methods} = setup();

    methods.setRefreshFailed();

    expect(state.refreshFailed()).toBe(true);
    expect(state.summaryItems()).toEqual([]);
  });

  it('clears the flag when a month load starts', () => {
    const {state, methods} = setup();
    methods.setRefreshFailed();

    methods.clearRefreshFailed();

    expect(state.refreshFailed()).toBe(false);
  });

  it('clears the flag when a summary arrives', () => {
    const {state, methods} = setup();
    methods.setRefreshFailed();

    methods.setSummaryItems([]);

    expect(state.refreshFailed()).toBe(false);
  });
});
