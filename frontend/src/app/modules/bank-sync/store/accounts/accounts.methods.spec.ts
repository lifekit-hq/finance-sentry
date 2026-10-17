import {signalState} from '@ngrx/signals';
import {beforeEach, describe, expect, it} from 'vitest';

import {
  ACCOUNTS_COLLAPSED_STORAGE_KEY,
  ACCOUNTS_OPEN_STORAGE_KEY,
} from '../../constants/accounts-disclosure/accounts-disclosure.constants';
import {accountsMethods} from './accounts.methods';
import {type AccountsState, initialAccountsState} from './accounts.state';

function setup() {
  const state = signalState<AccountsState>(initialAccountsState);
  return {state, methods: accountsMethods(state as never)};
}

describe('accountsMethods institution open state', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('restores what this device opened or closed', () => {
    localStorage.setItem(ACCOUNTS_OPEN_STORAGE_KEY, '{"truelayer:aib":false,"ibkr:ibkr-1":true}');
    const {state, methods} = setup();

    methods.restoreInstitutionOpen();

    expect(state.institutionOpen()).toEqual({'truelayer:aib': false, 'ibkr:ibkr-1': true});
  });

  it('keeps a bank collapsed before the record existed closed', () => {
    localStorage.setItem(ACCOUNTS_COLLAPSED_STORAGE_KEY, '["truelayer:aib"]');
    const {state, methods} = setup();

    methods.restoreInstitutionOpen();

    expect(state.institutionOpen()).toEqual({'truelayer:aib': false});
  });

  it('remembers a group opened or closed, whichever way it differs from its default', () => {
    const {state, methods} = setup();

    methods.setInstitutionOpen('ibkr:ibkr-1', true);
    methods.setInstitutionOpen('truelayer:aib', false);

    expect(state.institutionOpen()).toEqual({'ibkr:ibkr-1': true, 'truelayer:aib': false});
    expect(localStorage.getItem(ACCOUNTS_OPEN_STORAGE_KEY)).toBe(
      '{"ibkr:ibkr-1":true,"truelayer:aib":false}'
    );
  });

  it('keeps the latest choice for a group toggled twice', () => {
    const {state, methods} = setup();

    methods.setInstitutionOpen('a', false);
    methods.setInstitutionOpen('a', true);

    expect(state.institutionOpen()).toEqual({a: true});
  });
});
