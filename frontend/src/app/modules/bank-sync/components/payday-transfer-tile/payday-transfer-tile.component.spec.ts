import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {type PaydayTransfer} from '../../models/payday-transfer/payday-transfer.model';
import {PaydayTransferStore} from '../../store/payday-transfer/payday-transfer.store';
import {PaydayTransferTileComponent} from './payday-transfer-tile.component';

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

function render(transfer: Nullable<PaydayTransfer>, editing = false) {
  const store = {
    transfer: signal(transfer),
    visible: signal(transfer?.hasCard === true),
    editing: signal(editing),
    hasSaveError: signal(false),
    isSaveLoading: signal(false),
    saveErrorMessage: signal(''),
    setEditing: vi.fn((value: boolean) => store.editing.set(value)),
    saveSettings: vi.fn(),
  };
  TestBed.overrideComponent(PaydayTransferTileComponent, {
    set: {providers: [{provide: PaydayTransferStore, useValue: store}]},
  });
  const fixture = TestBed.createComponent(PaydayTransferTileComponent);
  fixture.detectChanges();
  return {fixture, store, el: fixture.nativeElement as HTMLElement};
}

const text = (el: HTMLElement, id: string) =>
  el.querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s+/g, ' ').trim();

describe('PaydayTransferTileComponent', () => {
  it('states every part of the breakdown', () => {
    const {el} = render(TRANSFER);

    expect(text(el, 'payday-headline')).toContain('€900.00');
    expect(text(el, 'payday-owed')).toContain('30,000.00');
    expect(text(el, 'payday-outflow')).toContain('15,000.00');
    expect(text(el, 'payday-total')).toContain('45,000.00');
    expect(text(el, 'payday-rate')).toContain('50.00');
    expect(text(el, 'payday-rate')).toContain('Monobank /bank/currency');
    expect(text(el, 'payday-exact')).toContain('€900.00');
    expect(text(el, 'payday-suggested')).toContain('€900.00');
    expect(text(el, 'payday-cushion')).toContain('€0.00');
  });

  it('renders nothing without a connected card', () => {
    const {el} = render({...TRANSFER, hasCard: false});

    expect(el.querySelector('[data-testid="payday-transfer-tile"]')).toBeNull();
  });

  it('keeps the hryvnia side and says so when the rate is unavailable', () => {
    const {el} = render({
      ...TRANSFER,
      rate: null,
      exactEur: null,
      suggestedEur: null,
      cushionEur: null,
    });

    expect(el.querySelector('[data-testid="payday-no-rate"]')).not.toBeNull();
    expect(text(el, 'payday-total')).toContain('45,000.00');
    expect(el.querySelector('[data-testid="payday-rate"]')).toBeNull();
  });

  it('opens the form with the current settings', () => {
    const {fixture, store, el} = render(TRANSFER);

    (el.querySelector('[data-testid="payday-edit"] button') as HTMLElement).click();
    fixture.detectChanges();

    expect(store.setEditing).toHaveBeenCalledWith(true);
    expect(fixture.componentInstance.form.getRawValue()).toEqual({
      fixedOutflowUah: '15000',
      roundingStepEur: '50',
    });
    expect(el.querySelector('[data-testid="payday-form"]')).not.toBeNull();
  });

  it('saves the typed settings as numbers', () => {
    const {fixture, store} = render(TRANSFER, true);
    fixture.componentInstance.form.setValue({fixedOutflowUah: '20000', roundingStepEur: '100'});

    fixture.componentInstance.save();

    expect(store.saveSettings).toHaveBeenCalledWith({fixedOutflowUah: 20000, roundingStepEur: 100});
  });

  it('does not save invalid text', () => {
    const {fixture, store} = render(TRANSFER, true);
    fixture.componentInstance.form.setValue({fixedOutflowUah: 'abc', roundingStepEur: '0'});

    fixture.componentInstance.save();

    expect(store.saveSettings).not.toHaveBeenCalled();
  });
});
