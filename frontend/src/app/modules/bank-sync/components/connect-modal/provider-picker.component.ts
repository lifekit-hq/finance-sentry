import {NgOptimizedImage} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {
  ButtonComponent,
  DialogActionsComponent,
  SelectableCardComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {
  HIDDEN_PROVIDERS,
  PROVIDER_CATALOG,
} from '../../../../shared/constants/providers/providers.constants';
import {
  type PickableProvider,
  type ProviderDescriptor,
} from '../../../../shared/models/provider/provider.model';
import {ConnectStore} from '../../store/connect/connect.store';
import {PROVIDER_PICKER_PROMPT} from './connect-modal.constants';

@Component({
  selector: 'fns-provider-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TagComponent,
    ButtonComponent,
    DialogActionsComponent,
    NgOptimizedImage,
    SelectableCardComponent,
  ],
  templateUrl: './provider-picker.component.html',
})
export class ProviderPickerComponent {
  public readonly store = inject(ConnectStore);

  /** The visible providers of the institution type chosen on the previous step. */
  public readonly providers = computed<readonly ProviderDescriptor[]>(() => {
    const type = this.store.institutionType();
    return PROVIDER_CATALOG.filter(
      p => p.institutionType === type && !HIDDEN_PROVIDERS.has(p.slug)
    );
  });

  public readonly prompt = computed(() =>
    this.store.institutionType() === 'crypto'
      ? PROVIDER_PICKER_PROMPT.crypto
      : PROVIDER_PICKER_PROMPT.bank
  );

  public readonly connected = computed(() => this.store.connectedProviders());

  public select(slug: string): void {
    this.store.selectPickedProvider(slug as PickableProvider);
  }

  public back(): void {
    this.store.setModalStep('type-picker');
  }
}
