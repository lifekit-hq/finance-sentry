import {signalState} from '@ngrx/signals';
import {beforeEach, describe, expect, it} from 'vitest';

import {ACCOUNTS_COLLAPSED_STORAGE_KEY} from '../../constants/accounts-disclosure/accounts-disclosure.constants';
import {accountsMethods} from './accounts.methods';
import {type AccountsState, initialAccountsState} from './accounts.state';

function setup() {
  const state = signalState<AccountsState>(initialAccountsState);
  return {state, methods: accountsMethods(state as never)};
}

describe('accountsMethods collapsed institutions', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('restores what this device collapsed', () => {
    localStorage.setItem(ACCOUNTS_COLLAPSED_STORAGE_KEY, '["truelayer:aib"]');
    const {state, methods} = setup();

    methods.restoreCollapsed();

    expect(state.collapsedInstitutions()).toEqual(['truelayer:aib']);
  });

  it('remembers a collapsed group, and forgets it when reopened', () => {
    const {state, methods} = setup();

    methods.setInstitutionOpen('truelayer:aib', false);
    expect(state.collapsedInstitutions()).toEqual(['truelayer:aib']);
    expect(localStorage.getItem(ACCOUNTS_COLLAPSED_STORAGE_KEY)).toBe('["truelayer:aib"]');

    methods.setInstitutionOpen('truelayer:aib', true);
    expect(state.collapsedInstitutions()).toEqual([]);
    expect(localStorage.getItem(ACCOUNTS_COLLAPSED_STORAGE_KEY)).toBe('[]');
  });

  it('does not duplicate a group collapsed twice', () => {
    const {state, methods} = setup();

    methods.setInstitutionOpen('a', false);
    methods.setInstitutionOpen('a', false);

    expect(state.collapsedInstitutions()).toEqual(['a']);
  });
});
