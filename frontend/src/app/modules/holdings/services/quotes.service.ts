import {Injectable} from '@angular/core';
import {ApiService} from '@lifekit-hq/core';
import {type Observable} from 'rxjs';

import {type QuoteDto} from '../models/quote/quote.model';

@Injectable({providedIn: 'root'})
export class QuotesService extends ApiService {
  constructor() {
    super('');
  }

  public getQuotes(tickers: string[]): Observable<QuoteDto[]> {
    return this.get<QuoteDto[]>('research/quotes', {tickers: tickers.join(',')});
  }
}
