import {registerLocaleData} from '@angular/common';
import localeEnGb from '@angular/common/locales/en-GB';
import {type EnvironmentProviders, LOCALE_ID, makeEnvironmentProviders} from '@angular/core';

import {APP_LOCALE} from '../../shared/constants/date/date.constants';

export function provideAppLocale(): EnvironmentProviders {
  registerLocaleData(localeEnGb, APP_LOCALE);
  return makeEnvironmentProviders([{provide: LOCALE_ID, useValue: APP_LOCALE}]);
}
