import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {CmnDialogService, ToastService} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {CloseAccountDialogComponent} from '../../components/close-account-dialog/close-account-dialog.component';
import {type AccountClosureOptions} from '../../models/account-closure/account-closure.model';
import {type UserProfile} from '../../models/settings/settings.model';
import {PushStore} from '../../store/push/push.store';
import {SettingsStore} from '../../store/settings/settings.store';
import {SettingsComponent} from './settings.component';

const profile = {
  firstName: 'Ada',
  email: 'ada@example.com',
  lastName: 'Lovelace',
  baseCurrency: 'USD',
  emailAlerts: true,
  lowBalanceAlerts: true,
  lowBalanceThreshold: 100,
  syncFailureAlerts: true,
  watchlistAnalystAlerts: false,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
  hideEmptyBalances: true,
  hideSmallBalances: true,
  smallBalanceThreshold: 1,
} as unknown as UserProfile;

const CAN_CLOSE: AccountClosureOptions = {canClose: true, reason: null, graceDays: 30};

function create(dialogResult?: string) {
  const toast = {show: vi.fn()};
  const store = {
    profile: signal<UserProfile | null>(profile),
    profileRevision: signal(0),
    closureOptions: signal<AccountClosureOptions | null>(CAN_CLOSE),
    saveProfile: vi.fn(),
    applyChange: vi.fn(),
    closeAccount: vi.fn(),
  };
  const dialog = {open: vi.fn(() => ({afterClosed: () => of(dialogResult)}))};
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: AuthStore, useValue: {canManageUsers: signal(false)}},
      {provide: ToastService, useValue: toast},
      {provide: CmnDialogService, useValue: dialog},
    ],
  });
  TestBed.overrideComponent(SettingsComponent, {
    set: {
      providers: [
        {provide: SettingsStore, useValue: store},
        {provide: PushStore, useValue: {}},
      ],
      template: '',
    },
  });
  const fixture = TestBed.createComponent(SettingsComponent);
  return {store, toast, dialog, fixture, component: fixture.componentInstance};
}

function setup() {
  const ctx = create();
  const form = ctx.component.assumptionsForm();
  if (!form) {
    throw new Error('assumptions form missing');
  }
  return {...ctx, form};
}

describe('SettingsComponent financial independence assumptions', () => {
  it('builds no form until the profile has loaded', () => {
    const {store, component} = create();
    store.profile.set(null);
    expect(component.assumptionsForm()).toBeNull();
  });

  it('seeds the controls with the stored fractions as percent text', () => {
    const {form} = setup();
    expect(form.getRawValue()).toEqual({safeWithdrawalRate: '4', realAnnualReturn: '5'});
  });

  it('sends both assumptions, converted to fractions, when the profile is saved', () => {
    const {store, component, form} = setup();
    form.controls.safeWithdrawalRate.setValue('3.5');
    form.controls.safeWithdrawalRate.markAsDirty();
    form.controls.realAnnualReturn.setValue('6');
    form.controls.realAnnualReturn.markAsDirty();
    component.saveAssumptions();
    expect(store.saveProfile).toHaveBeenCalledWith(
      expect.objectContaining({safeWithdrawalRate: 0.035, realAnnualReturn: 0.06})
    );
  });

  it('sends the stored fractions untouched when the assumptions were not edited', () => {
    const {store, component} = create();
    const stored = {...profile, safeWithdrawalRate: 0.03333, realAnnualReturn: 0.0456};
    store.profile.set(stored as unknown as UserProfile);
    component.saveAssumptions();
    expect(store.saveProfile).toHaveBeenCalledWith(
      expect.objectContaining({safeWithdrawalRate: 0.03333, realAnnualReturn: 0.0456})
    );
  });

  it.each([
    ['in range', '4', true],
    ['at the lower bound', '0.5', true],
    ['at the upper bound', '10', true],
    ['above the range', '12', false],
    ['below the range', '0.4', false],
    ['not a number', 'abc', false],
    ['empty', '', false],
  ])('withdrawal rate %s (%s) validity is %s', (_label, text, valid) => {
    const {form} = setup();
    form.controls.safeWithdrawalRate.setValue(text);
    expect(form.controls.safeWithdrawalRate.valid).toBe(valid);
  });

  it.each([
    ['in range', '6', true],
    ['zero', '0', true],
    ['at the upper bound', '15', true],
    ['negative', '-1', false],
    ['above the range', '16', false],
    ['empty', '', false],
    ['not a number', 'abc', false],
  ])('real return %s (%s) validity is %s', (_label, text, valid) => {
    const {form} = setup();
    form.controls.realAnnualReturn.setValue(text);
    expect(form.controls.realAnnualReturn.valid).toBe(valid);
  });

  it('blocks Save and reveals the errors until the value is corrected', () => {
    const {store, component, form} = setup();
    form.controls.safeWithdrawalRate.setValue('12');
    component.saveAssumptions();
    expect(store.saveProfile).not.toHaveBeenCalled();
    expect(form.controls.safeWithdrawalRate.touched).toBe(true);
    form.controls.safeWithdrawalRate.setValue('4');
    component.saveAssumptions();
    expect(store.saveProfile).toHaveBeenCalledTimes(1);
  });

  it('does not toast from the component on dispatch', () => {
    const {component, toast} = setup();
    component.saveAssumptions();
    expect(toast.show).not.toHaveBeenCalled();
  });

  it('keeps what the user typed when the profile is edited elsewhere', () => {
    const {store, component, form} = setup();
    form.controls.safeWithdrawalRate.setValue('12');
    store.profile.set({...profile, firstName: 'Grace'} as unknown as UserProfile);
    expect(component.assumptionsForm()).toBe(form);
    expect(form.controls.safeWithdrawalRate.value).toBe('12');
  });
});

describe('SettingsComponent Save assumptions button', () => {
  function render() {
    const store = {
      profile: signal<UserProfile | null>(profile),
      profileRevision: signal(0),
      avatarInitials: signal('AL'),
      fullName: signal('Ada Lovelace'),
      isProfileSaveLoading: signal(false),
      closureDescription: signal('Close your account and sign out everywhere.'),
      canCloseAccount: signal(true),
      isClosureLoading: signal(false),
      updateProfile: vi.fn(),
      saveProfile: vi.fn(),
      applyChange: vi.fn(),
    };
    const push = {
      pushEnabled: signal(false),
      canToggle: signal(false),
      serverNotice: signal(''),
      browserNotice: signal(''),
      errorMessage: signal(''),
      actionErrorMessage: signal(''),
      rows: signal([]),
      isBusy: signal(false),
      removingId: signal<string | null>(null),
      togglePush: vi.fn(),
      remove: vi.fn(),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {provide: AuthStore, useValue: {canManageUsers: signal(false)}},
        {provide: ToastService, useValue: {show: vi.fn()}},
      ],
    });
    TestBed.overrideComponent(SettingsComponent, {
      set: {
        providers: [
          {provide: SettingsStore, useValue: store},
          {provide: PushStore, useValue: push},
        ],
      },
    });
    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const saveButton = (): HTMLButtonElement => {
      const button = Array.from(el.querySelectorAll('button')).find(b =>
        b.textContent?.includes('Save assumptions')
      );
      if (!button) {
        throw new Error('Save assumptions button missing');
      }
      return button;
    };
    const type = (selector: string, text: string): void => {
      const input = el.querySelector<HTMLInputElement>(selector);
      if (!input) {
        throw new Error(`${selector} missing`);
      }
      input.value = text;
      input.dispatchEvent(new Event('input', {bubbles: true}));
      fixture.detectChanges();
    };
    return {store, fixture, saveButton, type};
  }

  it('is enabled while the assumptions are valid', () => {
    const {saveButton} = render();
    expect(saveButton().disabled).toBe(false);
  });

  it('is disabled once an invalid value is typed and enabled again when corrected', () => {
    const {saveButton, type} = render();
    type('#s-swr input', '12');
    expect(saveButton().disabled).toBe(true);
    type('#s-swr input', '4');
    expect(saveButton().disabled).toBe(false);
  });

  it('follows the second assumption as well', () => {
    const {saveButton, type} = render();
    type('#s-real-return input', 'abc');
    expect(saveButton().disabled).toBe(true);
    type('#s-real-return input', '6');
    expect(saveButton().disabled).toBe(false);
  });

  it('is enabled for a profile that loads after the first render', () => {
    const {store, fixture, saveButton} = render();
    store.profile.set(null);
    fixture.detectChanges();
    store.profile.set({...profile, safeWithdrawalRate: 0.05} as unknown as UserProfile);
    fixture.detectChanges();
    expect(saveButton().disabled).toBe(false);
  });
});

describe('SettingsComponent hide-below amount', () => {
  function setupThreshold(text: string) {
    const ctx = create();
    const control = ctx.component.smallThresholdControl();
    if (!control) {
      throw new Error('threshold control missing');
    }
    control.setValue(text);
    return {...ctx, control};
  }

  it('seeds the field with the stored amount', () => {
    const {control} = setupThreshold('1');
    expect(control.value).toBe('1');
  });

  it('applies a changed amount when the field is committed', () => {
    const {store, component} = setupThreshold('5');
    component.commitSmallThreshold();
    expect(store.applyChange).toHaveBeenCalledWith({smallBalanceThreshold: 5});
  });

  it('sends nothing for the amount already stored', () => {
    const {store, component} = setupThreshold('1.0');
    component.commitSmallThreshold();
    expect(store.applyChange).not.toHaveBeenCalled();
  });

  it.each(['0', '-2', '101', '', 'abc'])(
    'keeps %j on screen with its error and sends nothing',
    text => {
      const {store, component, control} = setupThreshold(text);
      component.commitSmallThreshold();
      expect(store.applyChange).not.toHaveBeenCalled();
      expect(control.invalid).toBe(true);
      expect(control.touched).toBe(true);
    }
  );
});

describe('SettingsComponent name form', () => {
  it('seeds the name controls from the stored profile', () => {
    const {component} = create();
    expect(component.nameForm()?.getRawValue()).toEqual({firstName: 'Ada', lastName: 'Lovelace'});
  });

  it('saves only the typed, trimmed names', () => {
    const {store, component} = create();
    const form = component.nameForm();
    form?.controls.firstName.setValue('  Grace ');
    form?.controls.lastName.setValue('Hopper');
    component.saveName();
    expect(store.saveProfile).toHaveBeenCalledWith({firstName: 'Grace', lastName: 'Hopper'});
  });

  it('does not send the typed names until Save is pressed', () => {
    const {store, component} = create();
    component.nameForm()?.controls.firstName.setValue('Grace');
    expect(store.saveProfile).not.toHaveBeenCalled();
    expect(store.applyChange).not.toHaveBeenCalled();
  });
});

describe('SettingsComponent low balance threshold', () => {
  it('applies a changed number when the field is left', () => {
    const {store, component} = create();
    component.thresholdControl()?.setValue('250.5');
    component.commitThreshold();
    expect(store.applyChange).toHaveBeenCalledWith({lowBalanceThreshold: 250.5});
  });

  it('sends nothing when the number is unchanged', () => {
    const {store, component} = create();
    component.commitThreshold();
    expect(store.applyChange).not.toHaveBeenCalled();
  });

  it.each([['abc'], [''], ['-5']])('puts the stored value back for %j', text => {
    const {store, component} = create();
    const control = component.thresholdControl();
    control?.setValue(text);
    component.commitThreshold();
    expect(store.applyChange).not.toHaveBeenCalled();
    expect(control?.value).toBe('100');
  });

  it('shows the kept value again after the store rolls a rejected number back', () => {
    const {store, component} = create();
    component.thresholdControl()?.setValue('250.5');
    component.commitThreshold();

    store.profileRevision.update(revision => revision + 1);

    expect(component.thresholdControl()?.value).toBe('100');
  });
});

describe('SettingsComponent appearance', () => {
  it('offers Petrol as the app default', () => {
    expect(create().component.appSeed).toBe('#175a6d');
  });
});

describe('SettingsComponent account closure', () => {
  it('asks for the typed email with the account email and the grace period', () => {
    const {dialog, component} = create();
    component.requestClose();
    expect(dialog.open).toHaveBeenCalledWith(
      CloseAccountDialogComponent,
      expect.objectContaining({data: {email: 'ada@example.com', purgeDays: 30}})
    );
  });

  it('closes the account with the email typed in the dialog', () => {
    const {store, component} = create('ada@example.com');
    component.requestClose();
    expect(store.closeAccount).toHaveBeenCalledWith('ada@example.com');
  });

  it('closes nothing when the dialog is cancelled', () => {
    const {store, component} = create(undefined);
    component.requestClose();
    expect(store.closeAccount).not.toHaveBeenCalled();
  });

  it('opens no dialog when this account may not be closed', () => {
    const {store, dialog, component} = create('ada@example.com');
    store.closureOptions.set({canClose: false, reason: 'You are the only owner.', graceDays: 30});
    component.requestClose();
    expect(dialog.open).not.toHaveBeenCalled();
    expect(store.closeAccount).not.toHaveBeenCalled();
  });
});
