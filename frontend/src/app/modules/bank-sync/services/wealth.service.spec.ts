import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {
  type AccountBalanceItem,
  type CardGroup,
  type HiddenReason,
  type Institution,
  type WealthSummaryResponse,
} from '../../../shared/models/wealth/wealth.model';
import {WealthService} from './wealth.service';

const account = (currency: string, hiddenReason?: HiddenReason): AccountBalanceItem => ({
  accountId: currency,
  bankName: 'Monobank',
  accountType: 'checking',
  accountNumberLast4: '1234',
  currency,
  provider: 'monobank',
  category: 'banking',
  currentBalance: 0,
  balanceInBaseCurrency: 0,
  syncStatus: 'synced',
  lastSyncTimestamp: null,
  hiddenReason,
});

const card = (
  cardType: string,
  accounts: AccountBalanceItem[],
  hiddenReason?: HiddenReason
): CardGroup => ({
  cardType,
  displayName: cardType,
  totalInBaseCurrency: 0,
  syncStatus: 'synced',
  accounts,
  hiddenReason,
});

const institution = (
  accounts: AccountBalanceItem[],
  cards: Nullable<CardGroup[]>
): Institution => ({
  institutionId: 'i1',
  provider: 'monobank',
  name: 'Monobank',
  category: 'banking',
  totalInBaseCurrency: 0,
  syncStatus: 'synced',
  lastSyncTimestamp: null,
  lastSuccessfulSyncTimestamp: null,
  accounts,
  cards,
});

const summary = (inst: Institution): WealthSummaryResponse => ({
  totalNetWorth: 0,
  baseCurrency: 'USD',
  categories: [
    {category: 'banking', totalInBaseCurrency: 0, institutionCount: 1, institutions: [inst]},
  ],
  appliedFilters: {category: null, provider: null},
});

describe('WealthService.getSummary', () => {
  let service: WealthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
      ],
    });
    service = TestBed.inject(WealthService);
    http = TestBed.inject(HttpTestingController);
  });

  const load = (body: WealthSummaryResponse): Institution => {
    let result!: WealthSummaryResponse;
    service.getSummary().subscribe(r => (result = r));
    http.expectOne('http://api.test/wealth/summary').flush(body);
    return result.categories[0].institutions[0];
  };

  it('drops empty Monobank pockets and cards, as Accounts always showed them', () => {
    const inst = load(
      summary(
        institution(
          [],
          [
            card('black', [account('UAH'), account('USD', 'empty')]),
            card('platinum', [account('UAH', 'empty')], 'empty'),
          ]
        )
      )
    );

    expect(inst.cards?.map(c => c.cardType)).toEqual(['black']);
    expect(inst.cards?.[0].accounts.map(a => a.currency)).toEqual(['UAH']);
  });

  it('keeps small rows and the flat account list: nothing new is hidden yet', () => {
    const accounts = [account('EUR', 'empty'), account('USD', 'small')];
    const inst = load(summary(institution(accounts, [card('black', [account('USD', 'small')])])));

    expect(inst.accounts).toEqual(accounts);
    expect(inst.cards?.[0].accounts).toHaveLength(1);
  });

  it('leaves an institution without cards untouched', () => {
    const inst = load(summary(institution([], null)));

    expect(inst.cards).toBeNull();
  });
});
