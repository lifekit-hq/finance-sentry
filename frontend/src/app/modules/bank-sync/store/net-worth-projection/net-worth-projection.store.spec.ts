import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {WealthService} from '../../services/wealth.service';
import {netWorthProjection} from './net-worth-projection.fixtures';
import {NetWorthProjectionStore} from './net-worth-projection.store';

describe('NetWorthProjectionStore', () => {
  let wealth: {getNetWorthProjection: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof NetWorthProjectionStore> {
    TestBed.configureTestingModule({
      providers: [
        NetWorthProjectionStore,
        {provide: WealthService, useValue: wealth},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(NetWorthProjectionStore);
  }

  beforeEach(() => {
    wealth = {getNetWorthProjection: vi.fn().mockReturnValue(of(netWorthProjection()))};
  });

  it('is loading until the projection arrives', () => {
    wealth.getNetWorthProjection.mockReturnValue(NEVER);
    const store = inject();
    expect(store.isLoading()).toBe(true);
    expect(store.visible()).toBe(false);
  });

  it('reports success and shows the line once loaded', () => {
    const store = inject();
    expect(store.status()).toBe('success');
    expect(store.projection()).toEqual(netWorthProjection());
    expect(store.visible()).toBe(true);
  });

  it('keeps the line hidden and records the error when the load fails', () => {
    wealth.getNetWorthProjection.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'UNKNOWN'}}))
    );
    const store = inject();
    expect(store.status()).toBe('error');
    expect(store.visible()).toBe(false);
    expect(store.errorMessage()).toBe('Failed to load the net worth projection.');
  });
});
