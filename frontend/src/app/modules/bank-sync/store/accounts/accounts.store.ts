import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {accountsComputed} from './accounts.computed';
import {accountsEffects, accountsHooks} from './accounts.effects';
import {accountsMethods} from './accounts.methods';
import {initialAccountsState} from './accounts.state';

export const AccountsStore = signalStore(
  withState(initialAccountsState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load accounts. Try again.'}),
  withMethods(accountsMethods),
  withComputed(accountsComputed),
  withMethods(accountsEffects),
  withHooks({onInit: accountsHooks})
);
