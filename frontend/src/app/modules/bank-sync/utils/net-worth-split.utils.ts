import {type NetWorthSnapshotDto, type NetWorthSplit} from '../models/dashboard/dashboard.model';

export class NetWorthSplitUtils {
  /** The snapshot's cash / invested split, or null when any part is missing (no split that day). */
  public static of(snapshot: NetWorthSnapshotDto): Nullable<NetWorthSplit> {
    const {cashTotal, brokerageInvested, cryptoInvested} = snapshot;
    if (cashTotal == null || brokerageInvested == null || cryptoInvested == null) {
      return null;
    }
    return {
      cash: cashTotal,
      brokerageInvested,
      cryptoInvested,
      invested: brokerageInvested + cryptoInvested,
    };
  }
}
