import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type CashflowProfile} from '../../models/cashflow-profile/cashflow-profile.model';
import {type CashflowProfileState} from './cashflow-profile.state';

export function cashflowProfileMethods(store: WritableStateSource<CashflowProfileState>) {
  return {
    setProfile(profile: CashflowProfile): void {
      patchState(store, {profile});
    },
  };
}
