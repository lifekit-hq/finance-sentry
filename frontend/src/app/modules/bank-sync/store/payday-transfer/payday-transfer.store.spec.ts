import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {ToastService} from '@lifekit-hq/ui';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type PaydayTransfer} from '../../models/payday-transfer/payday-transfer.model';
import {PaydayTransferService} from '../../services/payday-transfer.service';
import {PaydayTransferStore} from './payday-transfer.store';

const TRANSFER: PaydayTransfer = {
  hasCard: true,
  owedUah: 30000,
  fixedOutflowUah: 15000,
  totalUah: 45000,
  roundingStepEur: 50,
  rate: {rateBuy: 50, publishedAt: '2026-11-02T09:00:00Z', source: 'Monobank /bank/currency'},
  exactEur: 900,
  suggestedEur: 900,
  cushionEur: 0,
  balanceSyncedAt: null,
};

describe('PaydayTransferStore', () => {
  let service: {
    getPaydayTransfer: ReturnType<typeof vi.fn>;
    updateSettings: ReturnType<typeof vi.fn>;
  };
  let toast: {show: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof PaydayTransferStore> {
    TestBed.configureTestingModule({
      providers: [
        PaydayTransferStore,
        {provide: PaydayTransferService, useValue: service},
        {provide: ToastService, useValue: toast},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(PaydayTransferStore);
  }

  beforeEach(() => {
    service = {
      getPaydayTransfer: vi.fn().mockReturnValue(of(TRANSFER)),
      updateSettings: vi.fn().mockReturnValue(of({fixedOutflowUah: 20000, roundingStepEur: 100})),
    };
    toast = {show: vi.fn()};
  });

  it('is loading until the figure arrives', () => {
    service.getPaydayTransfer.mockReturnValue(NEVER);
    const store = inject();

    expect(store.isLoading()).toBe(true);
    expect(store.visible()).toBe(false);
  });

  it('shows the tile once a card exists', () => {
    const store = inject();

    expect(store.transfer()).toEqual(TRANSFER);
    expect(store.visible()).toBe(true);
    expect(store.hasRate()).toBe(true);
  });

  it('hides the tile when no card is connected', () => {
    service.getPaydayTransfer.mockReturnValue(of({...TRANSFER, hasCard: false}));

    expect(inject().visible()).toBe(false);
  });

  it('reports no rate when the bank rate is unavailable', () => {
    service.getPaydayTransfer.mockReturnValue(of({...TRANSFER, rate: null, suggestedEur: null}));

    expect(inject().hasRate()).toBe(false);
  });

  it('hides the tile when the load fails', () => {
    service.getPaydayTransfer.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'X'}}))
    );
    const store = inject();

    expect(store.hasError()).toBe(true);
    expect(store.visible()).toBe(false);
  });

  it('saves the settings, leaves edit mode, confirms and reloads the figure', () => {
    const store = inject();
    store.setEditing(true);

    store.saveSettings({fixedOutflowUah: 20000, roundingStepEur: 100});

    expect(service.updateSettings).toHaveBeenCalledWith({
      fixedOutflowUah: 20000,
      roundingStepEur: 100,
    });
    expect(store.editing()).toBe(false);
    expect(toast.show).toHaveBeenCalledWith('Payday transfer settings saved', 'success');
    expect(service.getPaydayTransfer).toHaveBeenCalledTimes(2);
  });

  it('stays in edit mode and reports the error when the save is rejected', () => {
    service.updateSettings.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'X'}}))
    );
    const store = inject();
    store.setEditing(true);

    store.saveSettings({fixedOutflowUah: 20000, roundingStepEur: 100});

    expect(store.editing()).toBe(true);
    expect(store.hasSaveError()).toBe(true);
    expect(toast.show).not.toHaveBeenCalled();
  });
});
