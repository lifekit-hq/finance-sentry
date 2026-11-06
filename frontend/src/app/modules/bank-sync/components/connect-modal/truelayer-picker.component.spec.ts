import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type TrueLayerProvider} from '../../models/truelayer/truelayer.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {ConnectStore} from '../../store/connect/connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {LOAD_PROVIDERS_ERROR} from './connect-modal.constants';
import {TrueLayerPickerComponent} from './truelayer-picker.component';

const AIB: TrueLayerProvider = {
  providerId: 'ob-aib',
  displayName: 'AIB',
  country: 'IE',
  logoUrl: null,
};
const BOI: TrueLayerProvider = {
  providerId: 'ob-boi',
  displayName: 'Bank of Ireland',
  country: 'IE',
  logoUrl: null,
};

function setup(listTrueLayerProviders: ReturnType<typeof vi.fn>) {
  const strategy: ConnectStrategy = {slug: 'truelayer', formComponent: class {}, submit: vi.fn()};
  TestBed.configureTestingModule({
    providers: [
      {provide: BankSyncService, useValue: {listTrueLayerProviders}},
      {
        provide: ConnectStore,
        useValue: {errorMessage: signal(''), connect: vi.fn(), setModalStep: vi.fn()},
      },
      {provide: CONNECT_STRATEGY, useValue: strategy},
    ],
  });
  const fixture = TestBed.createComponent(TrueLayerPickerComponent);
  fixture.detectChanges();
  return {fixture, cmp: fixture.componentInstance};
}

describe('TrueLayerPickerComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads the Irish bank list on creation and reports success', async () => {
    const list = vi.fn().mockReturnValue(of([AIB, BOI]));
    const {fixture, cmp} = setup(list);
    await fixture.whenStable();

    expect(list).toHaveBeenCalledWith('ie');
    expect(cmp.status()).toBe('success');
    expect(cmp.filteredProviders()).toEqual([AIB, BOI]);
  });

  it('is loading until the list answers', () => {
    const {cmp} = setup(vi.fn().mockReturnValue(new Subject<TrueLayerProvider[]>()));

    expect(cmp.status()).toBe('loading');
  });

  it('reloads for the chosen country and clears the search', async () => {
    const list = vi.fn().mockReturnValue(of([AIB]));
    const {fixture, cmp} = setup(list);
    await fixture.whenStable();
    cmp.searchControl.setValue('zzz');

    cmp.onCountryChange('uk');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(list).toHaveBeenLastCalledWith('uk');
    expect(cmp.searchControl.value).toBe('');
  });

  it('drops the in-flight request when the country changes', async () => {
    const ie = new Subject<TrueLayerProvider[]>();
    const uk = new Subject<TrueLayerProvider[]>();
    const list = vi.fn((country: string) => (country === 'ie' ? ie : uk));
    const {fixture, cmp} = setup(list);

    cmp.onCountryChange('uk');
    fixture.detectChanges();
    ie.next([AIB]);
    uk.next([BOI]);
    await fixture.whenStable();

    expect(cmp.filteredProviders()).toEqual([BOI]);
    expect(cmp.status()).toBe('success');
  });

  it('filters the list by the search term, ignoring case', async () => {
    const {fixture, cmp} = setup(vi.fn().mockReturnValue(of([AIB, BOI])));
    await fixture.whenStable();

    cmp.searchControl.setValue('  bank of ');

    expect(cmp.filteredProviders()).toEqual([BOI]);
  });

  it('reports an error status when the list cannot load', async () => {
    const {fixture, cmp} = setup(
      vi.fn().mockReturnValue(throwError(() => new Error('TrueLayer is down')))
    );
    await fixture.whenStable();

    expect(cmp.status()).toBe('error');
    expect(cmp.errorMessage()).toBe(LOAD_PROVIDERS_ERROR);
    expect(cmp.filteredProviders()).toEqual([]);
  });
});
