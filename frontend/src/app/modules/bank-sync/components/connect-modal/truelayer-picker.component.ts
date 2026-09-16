import {NgOptimizedImage} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject, signal} from '@angular/core';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
  SelectableCardComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';
import {finalize, type Subscription} from 'rxjs';

import {type TrueLayerProvider} from '../../models/truelayer/truelayer.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {ConnectStore} from '../../store/connect/connect.store';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {TRUELAYER_COUNTRIES} from './connect-modal.constants';

@Component({
  selector: 'fns-truelayer-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    NgOptimizedImage,
    ReactiveFormsModule,
    SelectableCardComponent,
    SkeletonComponent,
  ],
  templateUrl: './truelayer-picker.component.html',
})
export class TrueLayerPickerComponent {
  private readonly bankSync = inject(BankSyncService);
  private readonly strategy = inject(CONNECT_STRATEGY);
  private readonly searchTerm = signal<string>('');
  private providersSub: Nullable<Subscription> = null;

  public readonly store = inject(ConnectStore);

  public readonly countries = TRUELAYER_COUNTRIES;

  public readonly selectedCountry = signal<string>('ie');
  public readonly providers = signal<readonly TrueLayerProvider[]>([]);
  public readonly isLoading = signal<boolean>(false);
  public readonly loadError = signal<Nullable<string>>(null);

  public readonly searchControl = new FormControl<string>('', {nonNullable: true});

  public readonly filteredProviders = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const list = this.providers();
    return term ? list.filter(p => p.displayName.toLowerCase().includes(term)) : list;
  });

  constructor() {
    this.searchControl.valueChanges.subscribe(value => this.searchTerm.set(value ?? ''));
    this.loadProviders('ie');
  }

  public onCountryChange(country: string): void {
    this.selectedCountry.set(country);
    this.searchControl.setValue('');
    this.loadProviders(country);
  }

  public select(provider: TrueLayerProvider): void {
    this.store.connect({
      strategy: this.strategy,
      payload: {providerId: provider.providerId, providerName: provider.displayName},
    });
  }

  public back(): void {
    this.providersSub?.unsubscribe();
    this.store.setModalStep('provider-picker');
  }

  private loadProviders(country: string): void {
    this.providersSub?.unsubscribe();
    this.isLoading.set(true);
    this.loadError.set(null);
    this.providersSub = this.bankSync
      .listTrueLayerProviders(country)
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: list => this.providers.set(list),
        error: () =>
          this.loadError.set(
            'Failed to load bank list. Check that the backend is configured with TrueLayer credentials.'
          ),
      });
  }
}
