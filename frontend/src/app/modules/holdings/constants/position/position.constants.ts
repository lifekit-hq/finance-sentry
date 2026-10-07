import {type PositionAssetClass} from '../../models/position/position.model';

/** Brokerage instrument type of a listed stock - the only rows a research quote can price. */
export const EQUITY_INSTRUMENT_TYPE = 'STK';

/** Backend `AssetClassNormalizer` bucket -> Holdings slice; anything unlisted (commodities, other) stays with equities. */
export const BROKERAGE_ASSET_CLASS: ReadonlyMap<string, PositionAssetClass> = new Map<
  string,
  PositionAssetClass
>([
  ['Cash', 'cash'],
  ['Bonds', 'bonds'],
  ['RealEstate', 'realEstate'],
  ['Crypto', 'crypto'],
]);
