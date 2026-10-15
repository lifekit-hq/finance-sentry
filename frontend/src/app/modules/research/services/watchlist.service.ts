import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type AddWatchlistRequest, type WatchlistItem} from '../models/watchlist/watchlist.model';

@Injectable({providedIn: 'root'})
export class WatchlistService extends ApiService {
  constructor() {
    super('research/watchlist');
  }

  public getItems(): Observable<WatchlistItem[]> {
    return this.get<WatchlistItem[]>('');
  }

  public add(payload: AddWatchlistRequest): Observable<WatchlistItem> {
    return this.post<WatchlistItem>('', payload);
  }

  public remove(id: string): Observable<void> {
    return this.delete<void>(id);
  }
}
