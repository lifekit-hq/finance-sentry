import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {dossierComputed} from './dossier.computed';
import {dossierEffects, dossierHooks} from './dossier.effects';
import {dossierMethods} from './dossier.methods';
import {initialDossierState} from './dossier.state';

export const DossierStore = signalStore(
  withState(initialDossierState),
  withNamedAsyncStatus('dossier', {defaultErrorMessage: 'Failed to load asset dossier.'}),
  withNamedAsyncStatus('ledgerRead', {defaultErrorMessage: "Failed to load Ledger's read."}),
  withNamedAsyncStatus('quote'),
  withMethods(dossierMethods),
  withComputed(dossierComputed),
  withMethods(dossierEffects),
  withHooks(dossierHooks)
);
