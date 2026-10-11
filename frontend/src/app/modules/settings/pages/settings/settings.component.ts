import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import {FormControl, FormGroup, FormsModule, ReactiveFormsModule} from '@angular/forms';
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
import {APP_SEED} from '../../constants/appearance/appearance.constants';
import {
  REAL_ANNUAL_RETURN_MAX_PERCENT,
  REAL_ANNUAL_RETURN_MIN_PERCENT,
  SAFE_WITHDRAWAL_RATE_MAX_PERCENT,
  SAFE_WITHDRAWAL_RATE_MIN_PERCENT,
} from '../../constants/fire/fire-assumptions.constants';
import {type ThemeChoice} from '../../models/appearance/appearance.model';
import {type BaseCurrency, type ThemePreference} from '../../models/settings/settings.model';
import {AppearanceStore} from '../../store/appearance/appearance.store';
import {PushStore} from '../../store/push/push.store';
import {SettingsStore} from '../../store/settings/settings.store';
import {percentRange} from '../../validators/percent-range.validator';

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

@Component({
  selector: 'fns-settings',
  imports: [
    PageContainerComponent,
    InputHintsDirective,
    ButtonComponent,
    FormFieldComponent,
    FormsModule,
    InputComponent,
    ReactiveFormsModule,
    RelativeTimePipe,
    SelectComponent,
    TagComponent,
    ToggleComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [SettingsStore, PushStore, AppearanceStore],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
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
  public readonly appearance = inject(AppearanceStore);
  public readonly appSeed = APP_SEED;
  public readonly currencyOptions = CURRENCY_OPTIONS;
  public readonly themeOptions = THEME_OPTIONS;
  public readonly canManageUsers = this.authStore.canManageUsers;
  public readonly withdrawalRateHint = `Between ${SAFE_WITHDRAWAL_RATE_MIN_PERCENT}% and ${SAFE_WITHDRAWAL_RATE_MAX_PERCENT}%. The classic rule is 4%.`;
  public readonly realReturnHint = `Between ${REAL_ANNUAL_RETURN_MIN_PERCENT}% and ${REAL_ANNUAL_RETURN_MAX_PERCENT}%, after inflation.`;
  public readonly withdrawalRateError = `Enter a number from ${SAFE_WITHDRAWAL_RATE_MIN_PERCENT} to ${SAFE_WITHDRAWAL_RATE_MAX_PERCENT}.`;
  public readonly realReturnError = `Enter a number from ${REAL_ANNUAL_RETURN_MIN_PERCENT} to ${REAL_ANNUAL_RETURN_MAX_PERCENT}.`;

  public readonly profileLoaded = computed(() => this.store.profile() !== null);

  // Built once the profile has loaded (not on every profile edit, which would reset the typed text).
  // The controls hold percent text as typed; the fraction conversion happens only when saving.
  public readonly assumptionsForm = computed(() => {
    if (!this.profileLoaded()) {
      return null;
    }
    const profile = untracked(() => this.store.profile());
    return new FormGroup({
      safeWithdrawalRate: new FormControl(
        PercentUtils.fromFraction(profile?.safeWithdrawalRate ?? 0),
        {
          nonNullable: true,
          validators: [
            percentRange(SAFE_WITHDRAWAL_RATE_MIN_PERCENT, SAFE_WITHDRAWAL_RATE_MAX_PERCENT),
          ],
        }
      ),
      realAnnualReturn: new FormControl(PercentUtils.fromFraction(profile?.realAnnualReturn ?? 0), {
        nonNullable: true,
        validators: [percentRange(REAL_ANNUAL_RETURN_MIN_PERCENT, REAL_ANNUAL_RETURN_MAX_PERCENT)],
      }),
    });
  });

  public saveProfile(): void {
    const p = this.store.profile();
    const form = this.assumptionsForm();
    if (!p || !form) {
      return;
    }
    if (form.invalid) {
      form.markAllAsTouched();
      return;
    }
    const assumptions = form.getRawValue();
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
      safeWithdrawalRate:
        PercentUtils.toFraction(
          assumptions.safeWithdrawalRate,
          SAFE_WITHDRAWAL_RATE_MIN_PERCENT,
          SAFE_WITHDRAWAL_RATE_MAX_PERCENT
        ) ?? p.safeWithdrawalRate,
      realAnnualReturn:
        PercentUtils.toFraction(
          assumptions.realAnnualReturn,
          REAL_ANNUAL_RETURN_MIN_PERCENT,
          REAL_ANNUAL_RETURN_MAX_PERCENT
        ) ?? p.realAnnualReturn,
    });
  }

  public onThemeChoice(event: Event): void {
    this.appearance.choose((event as CustomEvent<ThemeChoice>).detail);
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
