import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {pushComputed} from './push.computed';
import {pushEffects, pushHooks} from './push.effects';
import {pushMethods} from './push.methods';
import {initialPushState} from './push.state';

export const PushStore = signalStore(
  withState(initialPushState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load push settings.'}),
  withMethods(pushMethods),
  withComputed(pushComputed),
  withMethods(pushEffects),
  withHooks({onInit: pushHooks})
);
