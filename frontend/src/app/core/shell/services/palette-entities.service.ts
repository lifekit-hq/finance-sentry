import {inject, Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {catchError, forkJoin, map, type Observable, of} from 'rxjs';

import {BankSyncService} from '../../../modules/bank-sync/services/bank-sync.service';
import {PositionsService} from '../../../modules/holdings/services/positions.service';
import {type PaletteEntities, type WatchlistEntry} from '../models/palette-entities.model';

/** Reads the three existing endpoints behind the palette's entity items; a failed read yields none. */
@Injectable({providedIn: 'root'})
export class PaletteEntitiesService extends ApiService {
  private readonly positions = inject(PositionsService);
  private readonly bankSync = inject(BankSyncService);

  constructor() {
    super('research/watchlist');
  }

  public load(): Observable<PaletteEntities> {
    const holdings$ = this.positions.getPositions().pipe(catchError(() => of([])));
    const watchlist$ = this.get<WatchlistEntry[]>('').pipe(catchError(() => of([])));
    const accounts$ = this.bankSync.getAccounts().pipe(
      map(response => response.accounts),
      catchError(() => of([]))
    );
    return forkJoin([holdings$, watchlist$, accounts$]).pipe(
      map(([holdings, watchlist, accounts]) => ({holdings, watchlist, accounts}))
    );
  }
}
