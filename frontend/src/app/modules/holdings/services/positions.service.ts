import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {forkJoin, map, type Observable, of} from 'rxjs';
import {catchError} from 'rxjs/operators';

import {
  type BrokerageHoldingsDto,
  type CryptoHoldingsDto,
  type Position,
} from '../models/position/position.model';

const PERCENT_SCALE = 100;

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

@Injectable({providedIn: 'root'})
export class PositionsService extends ApiService {
  constructor() {
    super('');
  }

  public getPositions(): Observable<Position[]> {
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
            provider: brokerage?.provider ?? 'ibkr',
            quantity: p.quantity,
            currentValue: p.usdValue,
            currentPrice: p.quantity > 0 ? p.usdValue / p.quantity : 0,
            pnlPercent: providerPnlPercent(p.usdValue, costBasisUsd),
          };
        });

        // Crypto venues return no P&L, and our reconstructed crypto cost basis differs
        // from the exchange's own figure — so we do not surface a crypto P&L.
        const cryptoPositions: Position[] = (crypto?.holdings ?? []).map(h => ({
          symbol: h.asset,
          provider: h.provider,
          quantity: h.freeQuantity + h.lockedQuantity,
          currentValue: h.usdValue,
          currentPrice:
            h.freeQuantity + h.lockedQuantity > 0
              ? h.usdValue / (h.freeQuantity + h.lockedQuantity)
              : 0,
          pnlPercent: null,
        }));

        return [...brokeragePositions, ...cryptoPositions];
      })
    );
  }
}
