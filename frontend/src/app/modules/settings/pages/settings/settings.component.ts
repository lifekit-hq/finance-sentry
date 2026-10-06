import {ChangeDetectionStrategy, Component, inject, signal, ViewContainerRef} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {Router} from '@angular/router';
import {
  ButtonComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  FormFieldComponent,
  InputComponent,
  PageHeaderComponent,
  SelectComponent,
  TagComponent,
  ToastService,
  ToggleComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {RelativeTimePipe} from '../../../../shared/pipes/relative-time.pipe';
import {AuthStore} from '../../../auth/store/auth.store';
import {type BaseCurrency, type ThemePreference} from '../../models/settings/settings.model';
import {PushStore} from '../../store/push/push.store';
import {SettingsStore} from '../../store/settings/settings.store';

const CURRENCY_OPTIONS: {value: BaseCurrency; label: string}[] = [
  {value: 'USD', label: 'USD — US Dollar'},
  {value: 'EUR', label: 'EUR — Euro'},
  {value: 'GBP', label: 'GBP — British Pound'},
  {value: 'UAH', label: 'UAH — Ukrainian Hryvnia'},
  {value: 'BTC', label: 'BTC — Bitcoin'},
];

const THEME_OPTIONS: {value: ThemePreference; label: string}[] = [
  {value: 'system', label: 'System default'},
  {value: 'light', label: 'Light'},
  {value: 'dark', label: 'Dark'},
];

const MIN_PASSWORD_LENGTH = 8;

@Component({
  selector: 'fns-settings',
  imports: [
    ButtonComponent,
    FormFieldComponent,
    FormsModule,
    InputComponent,
    PageHeaderComponent,
    RelativeTimePipe,
    SelectComponent,
    TagComponent,
    ToggleComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [SettingsStore, PushStore],
  templateUrl: './settings.component.html',
})
export class SettingsComponent {
  private readonly authStore = inject(AuthStore);
  private readonly dialog = inject(CmnDialogService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly viewContainerRef = inject(ViewContainerRef);

  public readonly store = inject(SettingsStore);
  public readonly push = inject(PushStore);
  public readonly currencyOptions = CURRENCY_OPTIONS;
  public readonly themeOptions = THEME_OPTIONS;
  public readonly canManageUsers = this.authStore.canManageUsers;

  public readonly pwCurrent = signal('');
  public readonly pwNext = signal('');
  public readonly pwConfirm = signal('');

  public saveProfile(): void {
    const p = this.store.profile();
    if (!p) {
      return;
    }
    this.store.saveProfile({
      firstName: p.firstName,
      lastName: p.lastName,
      baseCurrency: p.baseCurrency,
      theme: p.theme,
      emailAlerts: p.emailAlerts,
      lowBalanceAlerts: p.lowBalanceAlerts,
      lowBalanceThreshold: p.lowBalanceThreshold,
      syncFailureAlerts: p.syncFailureAlerts,
    });
    this.toast.show('Profile saved successfully', 'success');
  }

  public changePassword(): void {
    if (!this.pwCurrent()) {
      this.toast.show('Enter your current password', 'error');
      return;
    }
    if (this.pwNext().length < MIN_PASSWORD_LENGTH) {
      this.toast.show('New password must be at least 8 characters', 'error');
      return;
    }
    if (this.pwNext() !== this.pwConfirm()) {
      this.toast.show('Passwords do not match', 'error');
      return;
    }
    this.store.changePassword({
      currentPassword: this.pwCurrent(),
      newPassword: this.pwNext(),
    });
    this.pwCurrent.set('');
    this.pwNext.set('');
    this.pwConfirm.set('');
    this.toast.show('Password updated', 'success');
  }

  public openPeople(): void {
    void this.router.navigateByUrl(AppRoute.SettingsPeople);
  }

  public signOut(): void {
    this.authStore.logout();
  }

  public requestDeleteAccount(): void {
    const ref = this.dialog.open<boolean>(ConfirmDialogComponent, {
      data: {
        title: 'Delete your account?',
        message:
          'All your connected accounts, transactions, and settings will be permanently erased. This action cannot be reversed.',
        confirmLabel: 'Delete Forever',
        cancelLabel: 'Cancel',
        confirmVariant: 'destructive',
      },
      size: 'sm',
      viewContainerRef: this.viewContainerRef,
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed !== true) {
          return;
        }
        this.toast.show('Account deletion requested', 'warning');
      });
  }
}
