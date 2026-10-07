import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  ViewContainerRef,
} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {Router} from '@angular/router';
import {
  ButtonComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  FormFieldComponent,
  InputComponent,
  PageContainerComponent,
  RelativeTimePipe,
  SelectComponent,
  TagComponent,
  ToastService,
  ToggleComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {PercentUtils} from '../../../../shared/utils/percent.utils';
import {AuthStore} from '../../../auth/store/auth.store';
import {
  REAL_ANNUAL_RETURN_MAX_PERCENT,
  REAL_ANNUAL_RETURN_MIN_PERCENT,
  SAFE_WITHDRAWAL_RATE_MAX_PERCENT,
  SAFE_WITHDRAWAL_RATE_MIN_PERCENT,
} from '../../constants/fire/fire-assumptions.constants';
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
    PageContainerComponent,
    InputHintsDirective,
    ButtonComponent,
    FormFieldComponent,
    FormsModule,
    InputComponent,
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
  private readonly withdrawalDraft = signal<string | null>(null);
  private readonly realReturnDraft = signal<string | null>(null);

  public readonly store = inject(SettingsStore);
  public readonly push = inject(PushStore);
  public readonly currencyOptions = CURRENCY_OPTIONS;
  public readonly themeOptions = THEME_OPTIONS;
  public readonly canManageUsers = this.authStore.canManageUsers;
  public readonly withdrawalRateHint = `Between ${SAFE_WITHDRAWAL_RATE_MIN_PERCENT}% and ${SAFE_WITHDRAWAL_RATE_MAX_PERCENT}%. The classic rule is 4%.`;
  public readonly realReturnHint = `Between ${REAL_ANNUAL_RETURN_MIN_PERCENT}% and ${REAL_ANNUAL_RETURN_MAX_PERCENT}%, after inflation.`;

  public readonly withdrawalRateError = computed(() => {
    const draft = this.withdrawalDraft();
    return draft !== null && this.parseWithdrawalRate(draft) === null
      ? `Enter a number from ${SAFE_WITHDRAWAL_RATE_MIN_PERCENT} to ${SAFE_WITHDRAWAL_RATE_MAX_PERCENT}.`
      : '';
  });
  public readonly realReturnError = computed(() => {
    const draft = this.realReturnDraft();
    return draft !== null && this.parseRealReturn(draft) === null
      ? `Enter a number from ${REAL_ANNUAL_RETURN_MIN_PERCENT} to ${REAL_ANNUAL_RETURN_MAX_PERCENT}.`
      : '';
  });
  public readonly assumptionsInvalid = computed(
    () => this.withdrawalRateError() !== '' || this.realReturnError() !== ''
  );

  public readonly pwCurrent = signal('');
  public readonly pwNext = signal('');
  public readonly pwConfirm = signal('');

  public saveProfile(): void {
    const p = this.store.profile();
    if (!p || this.assumptionsInvalid()) {
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
      watchlistAnalystAlerts: p.watchlistAnalystAlerts,
      safeWithdrawalRate: p.safeWithdrawalRate,
      realAnnualReturn: p.realAnnualReturn,
    });
    this.toast.show('Profile saved successfully', 'success');
  }

  // The typed text is kept as a draft so a rejected value stays visible beside its error; only a
  // value inside its bounds ever reaches the profile, and Save stays off until both are valid.
  public setWithdrawalRate(text: string): void {
    this.withdrawalDraft.set(text);
    const rate = this.parseWithdrawalRate(text);
    if (rate !== null) {
      this.store.updateProfile({safeWithdrawalRate: rate});
    }
  }

  public setRealReturn(text: string): void {
    this.realReturnDraft.set(text);
    const rate = this.parseRealReturn(text);
    if (rate !== null) {
      this.store.updateProfile({realAnnualReturn: rate});
    }
  }

  public withdrawalRateText(storedFraction: number): string {
    return this.withdrawalDraft() ?? PercentUtils.fromFraction(storedFraction);
  }

  public realReturnText(storedFraction: number): string {
    return this.realReturnDraft() ?? PercentUtils.fromFraction(storedFraction);
  }

  private parseWithdrawalRate(text: string): number | null {
    return PercentUtils.toFraction(
      text,
      SAFE_WITHDRAWAL_RATE_MIN_PERCENT,
      SAFE_WITHDRAWAL_RATE_MAX_PERCENT
    );
  }

  private parseRealReturn(text: string): number | null {
    return PercentUtils.toFraction(
      text,
      REAL_ANNUAL_RETURN_MIN_PERCENT,
      REAL_ANNUAL_RETURN_MAX_PERCENT
    );
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
