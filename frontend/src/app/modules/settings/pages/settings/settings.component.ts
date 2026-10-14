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
  AppearanceSettingsComponent,
  ButtonComponent,
  CmnDialogService,
  DangerZoneComponent,
  FormFieldComponent,
  InputComponent,
  PageContainerComponent,
  RelativeTimePipe,
  SelectComponent,
  SettingRowComponent,
  SettingsSectionComponent,
  TagComponent,
  ToggleComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {PercentUtils} from '../../../../shared/utils/percent.utils';
import {AuthStore} from '../../../auth/store/auth.store';
import {CloseAccountDialogComponent} from '../../components/close-account-dialog/close-account-dialog.component';
import {APP_SEED} from '../../constants/appearance/appearance.constants';
import {
  REAL_ANNUAL_RETURN_MAX_PERCENT,
  REAL_ANNUAL_RETURN_MIN_PERCENT,
  SAFE_WITHDRAWAL_RATE_MAX_PERCENT,
  SAFE_WITHDRAWAL_RATE_MIN_PERCENT,
} from '../../constants/fire/fire-assumptions.constants';
import {type CloseAccountDialogData} from '../../models/account-closure/account-closure.model';
import {type BaseCurrency} from '../../models/settings/settings.model';
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

@Component({
  selector: 'fns-settings',
  imports: [
    AppearanceSettingsComponent,
    ButtonComponent,
    DangerZoneComponent,
    FormFieldComponent,
    FormsModule,
    InputComponent,
    InputHintsDirective,
    PageContainerComponent,
    ReactiveFormsModule,
    RelativeTimePipe,
    SelectComponent,
    SettingRowComponent,
    SettingsSectionComponent,
    TagComponent,
    ToggleComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [SettingsStore, PushStore],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './settings.component.html',
})
export class SettingsComponent {
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);

  public readonly store = inject(SettingsStore);
  public readonly push = inject(PushStore);
  public readonly appSeed = APP_SEED;
  public readonly currencyOptions = CURRENCY_OPTIONS;
  public readonly canManageUsers = this.authStore.canManageUsers;
  public readonly withdrawalRateHint = `Between ${SAFE_WITHDRAWAL_RATE_MIN_PERCENT}% and ${SAFE_WITHDRAWAL_RATE_MAX_PERCENT}%. The classic rule is 4%.`;
  public readonly realReturnHint = `Between ${REAL_ANNUAL_RETURN_MIN_PERCENT}% and ${REAL_ANNUAL_RETURN_MAX_PERCENT}%, after inflation.`;
  public readonly withdrawalRateError = `Enter a number from ${SAFE_WITHDRAWAL_RATE_MIN_PERCENT} to ${SAFE_WITHDRAWAL_RATE_MAX_PERCENT}.`;
  public readonly realReturnError = `Enter a number from ${REAL_ANNUAL_RETURN_MIN_PERCENT} to ${REAL_ANNUAL_RETURN_MAX_PERCENT}.`;

  public readonly profileLoaded = computed(() => this.store.profile() !== null);

  // Built once the profile has loaded (not on every profile edit, which would reset the typed text).
  public readonly nameForm = computed(() => {
    if (!this.profileLoaded()) {
      return null;
    }
    const profile = untracked(() => this.store.profile());
    return new FormGroup({
      firstName: new FormControl(profile?.firstName ?? '', {nonNullable: true}),
      lastName: new FormControl(profile?.lastName ?? '', {nonNullable: true}),
    });
  });

  // Rebuilt when a rejected change is rolled back, so the field shows the value that was kept.
  public readonly thresholdControl = computed(() => {
    this.store.profileRevision();
    if (!this.profileLoaded()) {
      return null;
    }
    const profile = untracked(() => this.store.profile());
    return new FormControl((profile?.lowBalanceThreshold ?? 0).toString(), {nonNullable: true});
  });

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

  public saveName(): void {
    const form = this.nameForm();
    if (!form) {
      return;
    }
    const {firstName, lastName} = form.getRawValue();
    this.store.saveProfile({firstName: firstName.trim(), lastName: lastName.trim()});
  }

  public saveAssumptions(): void {
    const p = this.store.profile();
    const form = this.assumptionsForm();
    if (!p || !form) {
      return;
    }
    if (form.invalid) {
      form.markAllAsTouched();
      return;
    }
    const {safeWithdrawalRate, realAnnualReturn} = form.controls;
    this.store.saveProfile({
      safeWithdrawalRate: safeWithdrawalRate.dirty
        ? (PercentUtils.toFraction(
            safeWithdrawalRate.value,
            SAFE_WITHDRAWAL_RATE_MIN_PERCENT,
            SAFE_WITHDRAWAL_RATE_MAX_PERCENT
          ) ?? p.safeWithdrawalRate)
        : p.safeWithdrawalRate,
      realAnnualReturn: realAnnualReturn.dirty
        ? (PercentUtils.toFraction(
            realAnnualReturn.value,
            REAL_ANNUAL_RETURN_MIN_PERCENT,
            REAL_ANNUAL_RETURN_MAX_PERCENT
          ) ?? p.realAnnualReturn)
        : p.realAnnualReturn,
    });
  }

  // A lone number field applies when the user leaves it or presses Enter, not on every keystroke.
  public commitThreshold(): void {
    const p = this.store.profile();
    const control = this.thresholdControl();
    if (!p || !control) {
      return;
    }
    const value = Number(control.value.trim());
    if (control.value.trim() === '' || !Number.isFinite(value) || value < 0) {
      control.setValue(p.lowBalanceThreshold.toString());
      return;
    }
    if (value !== p.lowBalanceThreshold) {
      this.store.applyChange({lowBalanceThreshold: value});
    }
  }

  public openPeople(): void {
    void this.router.navigateByUrl(AppRoute.SettingsPeople);
  }

  public signOut(): void {
    this.authStore.logout();
  }

  // Second step after the danger zone's own confirm: the account email, typed, closes it.
  public requestClose(): void {
    const profile = this.store.profile();
    const options = this.store.closureOptions();
    if (!profile || !options?.canClose) {
      return;
    }
    const data: CloseAccountDialogData = {email: profile.email, purgeDays: options.graceDays};
    this.dialog
      .open<string>(CloseAccountDialogComponent, {
        data,
        title: 'Close your account',
        size: 'md',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmEmail => {
        if (confirmEmail) {
          this.store.closeAccount(confirmEmail);
        }
      });
  }
}
