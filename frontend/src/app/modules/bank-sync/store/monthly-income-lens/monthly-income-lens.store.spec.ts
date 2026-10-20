import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {WealthService} from '../../services/wealth.service';
import {monthlyIncomeLens} from './monthly-income-lens.fixtures';
import {MonthlyIncomeLensStore} from './monthly-income-lens.store';

describe('MonthlyIncomeLensStore', () => {
  let wealth: {getMonthlyIncomeLens: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof MonthlyIncomeLensStore> {
    TestBed.configureTestingModule({
      providers: [
        MonthlyIncomeLensStore,
        {provide: WealthService, useValue: wealth},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(MonthlyIncomeLensStore);
  }

  beforeEach(() => {
    wealth = {getMonthlyIncomeLens: vi.fn().mockReturnValue(of(monthlyIncomeLens()))};
  });

  it('is loading until the lens arrives', () => {
    wealth.getMonthlyIncomeLens.mockReturnValue(NEVER);
    const store = inject();
    expect(store.isLoading()).toBe(true);
    expect(store.visible()).toBe(false);
  });

  it('reports success and shows the line once loaded', () => {
    const store = inject();
    expect(store.status()).toBe('success');
    expect(store.lens()).toEqual(monthlyIncomeLens());
    expect(store.visible()).toBe(true);
  });

  it('keeps the line hidden and records the error when the load fails', () => {
    wealth.getMonthlyIncomeLens.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'UNKNOWN'}}))
    );
    const store = inject();
    expect(store.status()).toBe('error');
    expect(store.visible()).toBe(false);
    expect(store.errorMessage()).toBe('Failed to load the monthly income figure.');
  });
});
