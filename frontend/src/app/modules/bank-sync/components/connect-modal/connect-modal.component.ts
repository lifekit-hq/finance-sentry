import {DialogRef} from '@angular/cdk/dialog';
import {NgComponentOutlet} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  Injector,
  type Type,
} from '@angular/core';

import {type Provider} from '../../../../shared/models/provider/provider.model';
import {ConnectStore} from '../../store/connect/connect.store';
import {CONNECT_STRATEGY, ConnectStrategyRegistry} from '../../strategies/connect-strategy.token';
import {ProviderPickerComponent} from './provider-picker.component';
import {SyncingStateComponent} from './syncing-state.component';
import {TypePickerComponent} from './type-picker.component';

@Component({
  selector: 'fns-connect-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgComponentOutlet, TypePickerComponent, ProviderPickerComponent, SyncingStateComponent],
  templateUrl: './connect-modal.component.html',
})
export class ConnectModalComponent {
  private readonly parentInjector = inject(Injector);
  private readonly registry = inject(ConnectStrategyRegistry);
  private readonly dialogRef = inject<DialogRef<unknown>>(DialogRef, {optional: true});

  public readonly store = inject(ConnectStore);

  public readonly formComponent = computed<Nullable<Type<unknown>>>(() => {
    const slug = this.providerSlugForCurrentStep();
    return slug ? this.registry.getBySlug(slug).formComponent : null;
  });

  public readonly formInjector = computed<Nullable<Injector>>(() => {
    const slug = this.providerSlugForCurrentStep();
    if (!slug) {
      return null;
    }
    const strategy = this.registry.getBySlug(slug);
    return Injector.create({
      providers: [{provide: CONNECT_STRATEGY, useValue: strategy}],
      parent: this.parentInjector,
    });
  });

  constructor() {
    effect(() => {
      if (this.store.modalStep() === 'closed') {
        this.dialogRef?.close();
      }
    });
  }

  private providerSlugForCurrentStep(): Nullable<Provider> {
    switch (this.store.modalStep()) {
      case 'monobank-form':
        return 'monobank';
      case 'truelayer-picker':
        return 'truelayer';
      case 'binance-form':
        return 'binance';
      case 'revolut-x-form':
        return 'revolut_x';
      case 'ibkr-form':
        return 'ibkr';
      default:
        return null;
    }
  }
}
