import {type NetWorthBridgeResponse} from '../../models/net-worth-bridge/net-worth-bridge.model';

export interface NetWorthBridgeState {
  bridge: Nullable<NetWorthBridgeResponse>;
}

export const initialNetWorthBridgeState: NetWorthBridgeState = {
  bridge: null,
};
