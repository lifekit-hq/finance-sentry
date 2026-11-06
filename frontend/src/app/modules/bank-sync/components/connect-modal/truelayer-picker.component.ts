import {NgOptimizedImage} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject, signal} from '@angular/core';
import {rxResource} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {ErrorMessageService, extractErrorCode} from '@lifekit-hq/core';
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
  private readonly strategy = inject(CONNECT_STRATEGY);
  private readonly bankSync = inject(BankSyncService);
  private readonly errorMessages = inject(ErrorMessageService);
  private readonly searchTerm = signal<string>('');

  public readonly store = inject(ConnectStore);

  public readonly countries = TRUELAYER_COUNTRIES;

  public readonly selectedCountry = signal<string>('ie');

  /** The bank list for the chosen country; a new country cancels the in-flight request. */
  public readonly providers = rxResource({
    params: this.selectedCountry,
    stream: ({params: country}) => this.bankSync.listTrueLayerProviders(country),
  });

  public readonly status = computed<AsyncStateStatus>(() => {
    switch (this.providers.status()) {
      case 'loading':
      case 'reloading':
        return 'loading';
      case 'error':
        return 'error';
      case 'resolved':
      case 'local':
        return 'success';
      default:
        return 'idle';
    }
  });

  public readonly errorMessage = computed(() => {
    if (this.status() !== 'error') {
      return '';
    }
    const failure = this.providers.error();
    const cause = failure instanceof Error && failure.cause ? failure.cause : failure;
    return this.errorMessages.resolve(extractErrorCode(cause)) ?? LOAD_PROVIDERS_ERROR;
  });

  public readonly searchControl = new FormControl<string>('', {nonNullable: true});

  public readonly filteredProviders = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
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
