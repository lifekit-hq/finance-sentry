import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type InstitutionType,
  type Provider,
} from '../../../../shared/models/provider/provider.model';
import {ConnectStore} from '../../store/connect/connect.store';
import {PROVIDER_PICKER_PROMPT} from './connect-modal.constants';
import {ProviderPickerComponent} from './provider-picker.component';

function buildStore(
  institutionType: Nullable<InstitutionType> = 'bank',
  connected: ReadonlySet<Provider> = new Set()
) {
  return {
    institutionType: signal<Nullable<InstitutionType>>(institutionType),
    connectedProviders: signal(connected),
    selectPickedProvider: vi.fn(),
    setModalStep: vi.fn(),
  };
}

function configure(store: ReturnType<typeof buildStore>): void {
  TestBed.configureTestingModule({providers: [{provide: ConnectStore, useValue: store}]});
}

describe('ProviderPickerComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('lists only visible bank providers for the bank type', () => {
    configure(buildStore('bank'));
    const fixture = TestBed.createComponent(ProviderPickerComponent);
    const providers = fixture.componentInstance.providers();
    expect(providers.map(p => p.slug).sort()).toEqual(['monobank', 'truelayer']);
    expect(providers.every(p => p.institutionType === 'bank')).toBe(true);
    expect(fixture.componentInstance.prompt()).toBe(PROVIDER_PICKER_PROMPT.bank);
  });

  it('lists the crypto exchanges for the crypto type', () => {
    configure(buildStore('crypto'));
    const fixture = TestBed.createComponent(ProviderPickerComponent);
    expect(
      fixture.componentInstance
        .providers()
        .map(p => p.slug)
        .sort()
    ).toEqual(['binance', 'revolut_x']);
    expect(fixture.componentInstance.prompt()).toBe(PROVIDER_PICKER_PROMPT.crypto);
  });

  it('lists nothing before a type is chosen', () => {
    configure(buildStore(null));
    const fixture = TestBed.createComponent(ProviderPickerComponent);
    expect(fixture.componentInstance.providers()).toEqual([]);
  });

  it('select() forwards to store.selectPickedProvider', () => {
    const store = buildStore('crypto');
    configure(store);
    const fixture = TestBed.createComponent(ProviderPickerComponent);
    fixture.componentInstance.select('revolut_x');
    expect(store.selectPickedProvider).toHaveBeenCalledWith('revolut_x');
  });

  it('back() returns to the type-picker step', () => {
    const store = buildStore();
    configure(store);
    const fixture = TestBed.createComponent(ProviderPickerComponent);
    fixture.componentInstance.back();
    expect(store.setModalStep).toHaveBeenCalledWith('type-picker');
  });

  it('connected() reflects the store signal', () => {
    const set = new Set<Provider>(['monobank']);
    const store = buildStore('bank', set);
    configure(store);
    const fixture = TestBed.createComponent(ProviderPickerComponent);
    expect(fixture.componentInstance.connected()).toBe(set);
  });
});
