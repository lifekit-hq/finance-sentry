import {type EnvironmentProviders, makeEnvironmentProviders} from '@angular/core';
import {TitleStrategy} from '@angular/router';

import {AppTitleStrategy} from '../title/app-title.strategy';

export function provideTitleStrategy(): EnvironmentProviders {
  return makeEnvironmentProviders([{provide: TitleStrategy, useExisting: AppTitleStrategy}]);
}
