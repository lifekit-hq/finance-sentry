import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type AssetDossierDto, type AssetLedgerReadDto} from '../models/dossier/dossier.model';

@Injectable({providedIn: 'root'})
export class DossierService extends ApiService {
  constructor() {
    super('');
  }

  public getDossier(symbol: string): Observable<AssetDossierDto> {
    return this.get<AssetDossierDto>(`research/assets/${encodeURIComponent(symbol)}/dossier`);
  }

  /** Cached "Ledger's read" — instant, never runs the agent. */
  public getLedgerRead(symbol: string): Observable<AssetLedgerReadDto> {
    return this.get<AssetLedgerReadDto>(
      `research/assets/${encodeURIComponent(symbol)}/narrative`
    );
  }

  /** Runs the agent loop and caches the result; `force` regenerates over a fresh cached copy. */
  public generateLedgerRead(symbol: string, force: boolean): Observable<AssetLedgerReadDto> {
    return this.post<AssetLedgerReadDto>(
      `research/assets/${encodeURIComponent(symbol)}/narrative?force=${force}`,
      {}
    );
  }
}
