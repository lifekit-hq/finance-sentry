import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {forkJoin, map, type Observable, of} from 'rxjs';
import {catchError} from 'rxjs/operators';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {HeadlineUtils} from '../../../shared/utils/headline.utils';
import {BROKERAGE_ASSET_CLASS} from '../constants/position/position.constants';
import {
  type BrokerageHoldingsDto,
  type BrokeragePositionDto,
  type CryptoHoldingDto,
  type CryptoHoldingsDto,
  type Position,
} from '../models/position/position.model';

const PERCENT_SCALE = 100;

/** The two holdings feeds, named as the headline's stale-source reasons name them. */
const BROKERAGE_SOURCE = 'brokerage';
const CRYPTO_SOURCE = 'crypto';
const HOLDINGS_CURRENCY = 'USD';

/** Every position across the feeds, and the quality of the total they add up to. */
export interface HoldingsResult {
  positions: Position[];
  /** The total's typed record; null when both feeds read cleanly and the API sent none. */
  headline: Nullable<Headline>;
}

// P&L % from the provider's own cost basis. Returns null when no usable cost
// basis is available, so callers render "no data" rather than a fabricated number.
function providerPnlPercent(
  currentValue: number,
  costBasisUsd: Nullable<number>
): Nullable<number> {
  if (costBasisUsd == null || costBasisUsd <= 0) {
    return null;
  }
  return ((currentValue - costBasisUsd) / costBasisUsd) * PERCENT_SCALE;
}

function providerPnlUsd(currentValue: number, costBasisUsd: Nullable<number>): Nullable<number> {
  if (costBasisUsd == null || costBasisUsd <= 0) {
    return null;
  }
  return currentValue - costBasisUsd;
}

function cryptoUnitPrice(holding: CryptoHoldingDto): Nullable<number> {
  if (holding.isFiat) {
    return null;
  }
  const quantity = holding.freeQuantity + holding.lockedQuantity;
  return quantity > 0 ? holding.usdValue / quantity : 0;
}

/** The provider's own currency and amount, kept only for a non-USD row that reported both. */
function nativeAmount(p: BrokeragePositionDto): Pick<Position, 'nativeCurrency' | 'nativeValue'> {
  const currency = p.currency?.toUpperCase();
  if (!currency || currency === 'USD' || p.nativeValue == null) {
    return {};
  }
  return {nativeCurrency: currency, nativeValue: p.nativeValue};
}

@Injectable({providedIn: 'root'})
export class PositionsService extends ApiService {
  constructor() {
    super('');
  }

  public getPositions(): Observable<Position[]> {
    return this.getHoldings().pipe(map(result => result.positions));
  }

  /**
   * Positions from both feeds with the headline of their total. A feed that fails to read is
   * left out of the positions as before, but now named on the headline as a stale source, so a
   * total that silently lacks a broken connection says so.
   */
  public getHoldings(): Observable<HoldingsResult> {
    const brokerage$ = this.get<BrokerageHoldingsDto>('brokerage/holdings').pipe(
      catchError(() => of(null))
    );

    const crypto$ = this.get<CryptoHoldingsDto>('crypto/holdings').pipe(catchError(() => of(null)));

    return forkJoin([brokerage$, crypto$]).pipe(
      map(([brokerage, crypto]) => {
        const brokeragePositions: Position[] = (brokerage?.positions ?? []).map(p => {
          // IBKR supplies avgCost directly; derive total cost basis when only the
          // per-unit average is returned.
          const costBasisUsd =
            p.costBasisUsd ?? (p.averageCostUsd != null ? p.averageCostUsd * p.quantity : null);
          return {
            symbol: p.symbol,
            provider: p.provider ?? brokerage?.provider ?? 'ibkr',
            instrumentType: p.instrumentType,
            quantity: p.quantity,
            currentValue: p.usdValue,
            currentPrice: p.quantity > 0 ? p.usdValue / p.quantity : 0,
            pnlPercent: providerPnlPercent(p.usdValue, costBasisUsd),
            pnlUsd: providerPnlUsd(p.usdValue, costBasisUsd),
            isVenueCash: false,
            assetClass: BROKERAGE_ASSET_CLASS.get(p.assetClass ?? '') ?? 'equity',
            ...nativeAmount(p),
            hiddenReason: p.hiddenReason ?? null,
          };
        });

        // Crypto venues return no P&L, and our reconstructed crypto cost basis differs
        // from the exchange's own figure — so we do not surface a crypto P&L.
        const cryptoPositions: Position[] = (crypto?.holdings ?? []).map(h => ({
          symbol: h.asset,
          provider: h.provider,
          instrumentType: null,
          quantity: h.freeQuantity + h.lockedQuantity,
          currentValue: h.usdValue,
          currentPrice: cryptoUnitPrice(h),
          pnlPercent: null,
          pnlUsd: null,
          isVenueCash: h.isFiat,
          assetClass: h.isFiat ? 'cash' : 'crypto',
          hiddenReason: h.hiddenReason ?? null,
        }));

        const positions = [...brokeragePositions, ...cryptoPositions];
        const failed = [
          ...(brokerage ? [] : [BROKERAGE_SOURCE]),
          ...(crypto ? [] : [CRYPTO_SOURCE]),
        ];
        const bothFailed = !brokerage && !crypto;
        const total = positions.reduce((sum, p) => sum + p.currentValue, 0);
        const headline = HeadlineUtils.withStaleSources(
          HeadlineUtils.combine([brokerage?.headline, crypto?.headline]),
          failed,
          HOLDINGS_CURRENCY,
          bothFailed ? null : total
        );
        return {positions, headline};
      })
    );
  }
}
