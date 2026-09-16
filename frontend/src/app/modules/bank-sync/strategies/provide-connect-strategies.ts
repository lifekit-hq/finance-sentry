import {type EnvironmentProviders, makeEnvironmentProviders} from '@angular/core';

import {BinanceConnectStrategy} from './binance.strategy';
import {CONNECT_STRATEGIES, ConnectStrategyRegistry} from './connect-strategy.token';
import {IbkrConnectStrategy} from './ibkr.strategy';
import {MonobankConnectStrategy} from './monobank.strategy';
import {RevolutXConnectStrategy} from './revolut-x.strategy';
import {TrueLayerConnectStrategy} from './truelayer.strategy';

export function provideConnectStrategies(): EnvironmentProviders {
  return makeEnvironmentProviders([
    ConnectStrategyRegistry,
    {provide: CONNECT_STRATEGIES, multi: true, useExisting: MonobankConnectStrategy},
    {provide: CONNECT_STRATEGIES, multi: true, useExisting: TrueLayerConnectStrategy},
    {provide: CONNECT_STRATEGIES, multi: true, useExisting: BinanceConnectStrategy},
    {provide: CONNECT_STRATEGIES, multi: true, useExisting: RevolutXConnectStrategy},
    {provide: CONNECT_STRATEGIES, multi: true, useExisting: IbkrConnectStrategy},
  ]);
}
