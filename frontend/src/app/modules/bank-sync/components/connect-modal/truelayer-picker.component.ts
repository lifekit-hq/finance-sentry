import {NgOptimizedImage} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject, signal} from '@angular/core';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
  SelectableCardComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {type TrueLayerProvider} from '../../models/truelayer/truelayer.model';
import {ConnectStore} from '../../store/connect/connect.store';
import {TrueLayerPickerStore} from '../../store/truelayer-picker/truelayer-picker.store';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {TRUELAYER_COUNTRIES} from './connect-modal.constants';

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
  providers: [TrueLayerPickerStore],
})
export class TrueLayerPickerComponent {
  private readonly strategy = inject(CONNECT_STRATEGY);
  private readonly searchTerm = signal<string>('');

  public readonly store = inject(ConnectStore);

  public readonly countries = TRUELAYER_COUNTRIES;
  public readonly picker = inject(TrueLayerPickerStore);

  public readonly selectedCountry = signal<string>('ie');

  public readonly searchControl = new FormControl<string>('', {nonNullable: true});

  public readonly filteredProviders = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const list = this.picker.providers();
    return term ? list.filter(p => p.displayName.toLowerCase().includes(term)) : list;
  });

  constructor() {
    this.picker.loadProviders(this.selectedCountry());
    this.searchControl.valueChanges.subscribe(value => this.searchTerm.set(value ?? ''));
  }

  public onCountryChange(country: string): void {
    this.selectedCountry.set(country);
    this.picker.loadProviders(country);
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
