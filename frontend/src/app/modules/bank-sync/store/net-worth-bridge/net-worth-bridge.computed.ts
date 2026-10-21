import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type Headline} from '../../../../shared/models/headline/headline.model';
import {MoneyUtils} from '../../../../shared/utils/money.utils';
import {
  NET_WORTH_BRIDGE_DEGRADED_FALLBACK,
  NET_WORTH_BRIDGE_DEGRADED_REASON,
  NET_WORTH_BRIDGE_RESIDUAL_TOLERANCE_LABEL,
  NET_WORTH_BRIDGE_UNAVAILABLE_FALLBACK,
} from '../../constants/net-worth-bridge/net-worth-bridge.constants';
import {
  type NetWorthBridgeDegraded,
  type NetWorthBridgeResponse,
  type NetWorthBridgeStep,
  type NetWorthBridgeSummary,
} from '../../models/net-worth-bridge/net-worth-bridge.model';
import {NetWorthBridgeUtils} from '../../utils/net-worth-bridge.utils';

interface StateSignals {
  bridge: Signal<Nullable<NetWorthBridgeResponse>>;
}

export function netWorthBridgeComputed(store: StateSignals) {
  const messages = inject(ErrorMessageService);
  const steps = computed<NetWorthBridgeStep[]>(() => {
    const bridge = store.bridge();
    return bridge ? NetWorthBridgeUtils.steps(bridge) : [];
  });

  return {
    steps,
    /** End − Start with the bridge's quality and reasons, for its badge; null until a bridge loads. */
    headline: computed<Nullable<Headline>>(() => {
      const bridge = store.bridge();
      return bridge ? NetWorthBridgeUtils.headline(bridge) : null;
    }),
    /** Home's three-line reading; null until a bridge with figures loads. */
    summary: computed<Nullable<NetWorthBridgeSummary>>(() => {
      const bridge = store.bridge();
      return bridge ? NetWorthBridgeUtils.summary(bridge) : null;
    }),
    /** Why there is no bridge to draw (fewer than two snapshots in the window); null when there is one. */
    unavailableMessage: computed<Nullable<string>>(() => {
      const bridge = store.bridge();
      if (!bridge || steps().length > 0) {
        return null;
      }
      const code = bridge.reasons[0]?.code;
      return (code ? messages.resolve(code) : null) ?? NET_WORTH_BRIDGE_UNAVAILABLE_FALLBACK;
    }),
    /**
     * The DEGRADED state's reason in place, not only behind the badge: the registry's reason and how much
     * the known movements leave unexplained, against the tolerance. Null while the residual is within it.
     */
    degraded: computed<Nullable<NetWorthBridgeDegraded>>(() => {
      const bridge = store.bridge();
      if (!bridge?.residualAboveTolerance || bridge.residual == null) {
        return null;
      }
      const residual = MoneyUtils.format(bridge.residual, bridge.currency, {signed: true});
      return {
        reason:
          messages.resolve(NET_WORTH_BRIDGE_DEGRADED_REASON) ?? NET_WORTH_BRIDGE_DEGRADED_FALLBACK,
        detail:
          `${residual} is unexplained, more than ${NET_WORTH_BRIDGE_RESIDUAL_TOLERANCE_LABEL} of the ` +
          'starting value, so read the lines above it as approximate.',
      };
    }),
  };
}
