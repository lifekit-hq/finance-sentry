import {TestBed} from '@angular/core/testing';
import {ErrorMessageService} from '@lifekit-hq/core';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type IbkrFlexPreview} from '../../models/ibkr/ibkr.model';
import {IBKRService} from '../../services/ibkr.service';
import {IbkrConnectStore} from './ibkr-connect.store';

const PREVIEW: IbkrFlexPreview = {
  accountId: 'U1234567',
  fromDate: '2025-01-01',
  toDate: '2025-12-31',
  generatedAtUtc: '2026-01-05T14:30:15Z',
  openPositionsCount: 3,
  cashCurrencies: ['EUR', 'USD'],
  tradesCount: 12,
  cashTransactionsCount: 4,
};
const REQUEST = {token: 'tok', queryId: '123456'};

describe('IbkrConnectStore', () => {
  let ibkr: {validateFlex: ReturnType<typeof vi.fn>};
  let resolve: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    ibkr = {validateFlex: vi.fn()};
    resolve = vi.fn((code: string) => (code === 'IBKR_FLEX_INVALID_TOKEN' ? 'Bad token.' : null));
    TestBed.configureTestingModule({
      providers: [
        IbkrConnectStore,
        {provide: IBKRService, useValue: ibkr},
        {provide: ErrorMessageService, useValue: {resolve}},
      ],
    });
  });

  it('validate() stores the preview and flags it as shown', () => {
    ibkr.validateFlex.mockReturnValue(of(PREVIEW));
    const store = TestBed.inject(IbkrConnectStore);

    store.validate(REQUEST);

    expect(ibkr.validateFlex).toHaveBeenCalledWith(REQUEST);
    expect(store.hasPreview()).toBe(true);
    expect(store.preview()).toEqual(PREVIEW);
    expect(store.missingSections()).toEqual([]);
  });

  it('validate() maps a registered IBKR error code to its message', () => {
    ibkr.validateFlex.mockReturnValue(
      throwError(() => ({error: {errorCode: 'IBKR_FLEX_INVALID_TOKEN'}}))
    );
    const store = TestBed.inject(IbkrConnectStore);

    store.validate(REQUEST);

    expect(store.hasPreview()).toBe(false);
    expect(store.validationErrorMessage()).toBe('Bad token.');
  });

  it('validate() falls back to a generic message for an unregistered code', () => {
    ibkr.validateFlex.mockReturnValue(throwError(() => new Error('boom')));
    const store = TestBed.inject(IbkrConnectStore);

    store.validate(REQUEST);

    expect(store.validationErrorMessage()).toContain("Couldn't check your Flex query");
  });

  it('flags the sections that returned nothing', () => {
    ibkr.validateFlex.mockReturnValue(of({...PREVIEW, openPositionsCount: 0, cashCurrencies: []}));
    const store = TestBed.inject(IbkrConnectStore);

    store.validate(REQUEST);

    expect(store.missingSections()).toEqual(['Open Positions', 'Cash Report']);
  });

  it('resetValidation() clears preview and error', () => {
    ibkr.validateFlex.mockReturnValue(of(PREVIEW));
    const store = TestBed.inject(IbkrConnectStore);
    store.validate(REQUEST);

    store.resetValidation();

    expect(store.hasPreview()).toBe(false);
    expect(store.preview()).toBeNull();
    expect(store.validationErrorMessage()).toBe('');
  });

  it('setPath() records which connect path the user used', () => {
    const store = TestBed.inject(IbkrConnectStore);
    store.setPath('oauth');
    expect(store.path()).toBe('oauth');
  });
});
