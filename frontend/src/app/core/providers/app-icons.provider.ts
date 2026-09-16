import {type EnvironmentProviders} from '@angular/core';
import {provideCustomIcons} from '@lifekit-hq/ui';

export function provideAppIcons(): EnvironmentProviders {
  return provideCustomIcons({
    urls: {
      'provider-monobank': '/assets/providers/monobank.svg',

      'provider-binance': '/assets/providers/binance.svg',

      'provider-revolut_x': '/assets/providers/revolut_x.svg',

      'provider-ibkr': '/assets/providers/ibkr.svg',
    },
  });
}
