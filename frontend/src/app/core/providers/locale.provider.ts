import {type EnvironmentProviders, LOCALE_ID, makeEnvironmentProviders} from '@angular/core';

import {APP_LOCALE} from '../../shared/constants/date/date.constants';

export function provideAppLocale(): EnvironmentProviders {
  return makeEnvironmentProviders([{provide: LOCALE_ID, useValue: APP_LOCALE}]);
}
