import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type ChartRangeId,
  type DossierQuoteDto,
  type DossierUserTargetDto,
  type SetPriceTargetRequest,
} from '../models/dossier/dossier.model';

@Injectable({providedIn: 'root'})
export class DossierService extends ApiService {
  constructor() {
    super('');
  }

  public getDossier(symbol: string): Observable<AssetDossierDto> {
    return this.get<AssetDossierDto>(`research/assets/${encodeURIComponent(symbol)}/dossier`);
  }

  /** Live quote for one ticker; the endpoint answers with a list. */
  public getQuotes(symbol: string): Observable<DossierQuoteDto[]> {
    return this.get<DossierQuoteDto[]>(`research/quotes?tickers=${encodeURIComponent(symbol)}`);
  }

  /** Cached "Ledger's read" — instant, never runs the agent. */
  public getLedgerRead(symbol: string): Observable<AssetLedgerReadDto> {
    return this.get<AssetLedgerReadDto>(`research/assets/${encodeURIComponent(symbol)}/narrative`);
  }

  /** Runs the agent loop and caches the result; `force` regenerates over a fresh cached copy. */
  public generateLedgerRead(symbol: string, force: boolean): Observable<AssetLedgerReadDto> {
    return this.post<AssetLedgerReadDto>(
      `research/assets/${encodeURIComponent(symbol)}/narrative?force=${force}`,
      {}
    );
  }

  /** Daily closes for `range` with the S&P 500 rebased onto them, the user's fills and average cost. */
  public getChart(symbol: string, range: ChartRangeId): Observable<AssetChartDto> {
    return this.get<AssetChartDto>(`research/assets/${encodeURIComponent(symbol)}/chart`, {range});
  }

  /** Sets (or replaces) the user's own price target for the symbol. */
  public setTarget(
    symbol: string,
    request: SetPriceTargetRequest
  ): Observable<DossierUserTargetDto> {
    return this.put<DossierUserTargetDto>(
      `research/assets/${encodeURIComponent(symbol)}/target`,
      request
    );
  }

  public clearTarget(symbol: string): Observable<void> {
    return this.delete<void>(`research/assets/${encodeURIComponent(symbol)}/target`);
  }
}
