import {NgOptimizedImage} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject, signal} from '@angular/core';
import {rxResource} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  AsyncStateComponent,
  type AsyncStateStatus,
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
  SelectableCardComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {type TrueLayerProvider} from '../../models/truelayer/truelayer.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {ConnectStore} from '../../store/connect/connect.store';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {LOAD_PROVIDERS_ERROR, TRUELAYER_COUNTRIES} from './connect-modal.constants';

@Component({
  selector: 'fns-truelayer-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    InputHintsDirective,
    AlertComponent,
    AsyncStateComponent,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    NgOptimizedImage,
    ReactiveFormsModule,
    SelectableCardComponent,
  ],
  templateUrl: './truelayer-picker.component.html',
})
export class TrueLayerPickerComponent {
  private readonly bankSync = inject(BankSyncService);
  private readonly strategy = inject(CONNECT_STRATEGY);
  private readonly searchTerm = signal<string>('');

  public readonly store = inject(ConnectStore);

  public readonly countries = TRUELAYER_COUNTRIES;
  public readonly loadProvidersError = LOAD_PROVIDERS_ERROR;

  public readonly selectedCountry = signal<string>('ie');

  /** The bank list for the selected country; a country change cancels the in-flight request and loads again. */
  public readonly providers = rxResource({
    params: () => this.selectedCountry(),
    stream: ({params}) => this.bankSync.listTrueLayerProviders(params),
  });

  public readonly listStatus = computed<AsyncStateStatus>(() => {
    if (this.providers.isLoading()) {
      return 'loading';
    }
    return this.providers.error() ? 'error' : 'success';
  });

  public readonly searchControl = new FormControl<string>('', {nonNullable: true});

  public readonly filteredProviders = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    // `value()` throws while the resource is in its error state, so read it only when there is one.
    const list = this.providers.hasValue() ? this.providers.value() : [];
    return term ? list.filter(p => p.displayName.toLowerCase().includes(term)) : list;
  });

  constructor() {
    this.searchControl.valueChanges.subscribe(value => this.searchTerm.set(value ?? ''));
  }

  public onCountryChange(country: string): void {
    this.selectedCountry.set(country);
    this.searchControl.setValue('');
  }

  public select(provider: TrueLayerProvider): void {
    this.store.connect({
      strategy: this.strategy,
      payload: {providerId: provider.providerId, providerName: provider.displayName},
    });
  }

  public back(): void {
    this.store.setModalStep('provider-picker');
  }
}
