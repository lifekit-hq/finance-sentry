import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {whatsNewComputed} from './whats-new.computed';
import {whatsNewEffects, whatsNewHooks} from './whats-new.effects';
import {whatsNewMethods} from './whats-new.methods';
import {initialWhatsNewState} from './whats-new.state';

/** App-wide: the shell, the More page and the panel share one read of `whats-new.json` and one "seen" version. */
export const WhatsNewStore = signalStore(
  {providedIn: 'root'},
  withState(initialWhatsNewState),
  withMethods(whatsNewMethods),
  withComputed(whatsNewComputed),
  withMethods(whatsNewEffects),
  withHooks(whatsNewHooks)
);
