import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {subscriptionsComputed} from './subscriptions.computed';
import {subscriptionsEffects, subscriptionsHooks} from './subscriptions.effects';
import {subscriptionsMethods} from './subscriptions.methods';
import {initialSubscriptionsState} from './subscriptions.state';

export const SubscriptionsStore = signalStore(
  withState(initialSubscriptionsState),
  withNamedAsyncStatus('load', {defaultErrorMessage: 'Failed to load subscriptions.'}),
  withNamedAsyncStatus('add', {defaultErrorMessage: 'Failed to add.'}),
  withMethods(subscriptionsMethods),
  withComputed(subscriptionsComputed),
  withMethods(subscriptionsEffects),
  withHooks({onInit: subscriptionsHooks})
);
