import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type FireProjection} from '../../models/fire/fire.model';
import {WealthService} from '../../services/wealth.service';
import {FireStore} from './fire.store';

const PROJECTION: FireProjection = {
  status: 'Projected',
  target: 1_000_000,
  currentNetWorth: 250_000,
  monthlySavings: 2_000,
  annualSpend: 40_000,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
  projectedDate: '2041-03-26',
  monthsToFire: 150.2,
  hasStaleSleeves: false,
  baseCurrency: 'USD',
};

describe('FireStore', () => {
  let wealth: {getFireProjection: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof FireStore> {
    TestBed.configureTestingModule({
      providers: [
        FireStore,
        {provide: WealthService, useValue: wealth},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(FireStore);
  }

  beforeEach(() => {
    wealth = {getFireProjection: vi.fn().mockReturnValue(of(PROJECTION))};
  });

  it('is loading until the projection arrives', () => {
    wealth.getFireProjection.mockReturnValue(NEVER);

    const store = inject();

    expect(store.status()).toBe('loading');
    expect(store.isLoading()).toBe(true);
  });

  it('reports success and shows the tile once loaded', () => {
    const store = inject();

    expect(store.status()).toBe('success');
    expect(store.projection()).toEqual(PROJECTION);
    expect(store.visible()).toBe(true);
  });

  it('keeps the tile hidden and records the error when the load fails', () => {
    wealth.getFireProjection.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'UNKNOWN'}}))
    );

    const store = inject();

    expect(store.status()).toBe('error');
    expect(store.visible()).toBe(false);
    expect(store.errorMessage()).toBe('Failed to load FIRE projection.');
  });
});
