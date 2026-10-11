import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {ToastService} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {type UserProfile} from '../../models/settings/settings.model';
import {AppearanceStore} from '../../store/appearance/appearance.store';
import {PushStore} from '../../store/push/push.store';
import {SettingsStore} from '../../store/settings/settings.store';
import {SettingsComponent} from './settings.component';

const profile = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  baseCurrency: 'USD',
  theme: 'system',
  emailAlerts: true,
  lowBalanceAlerts: true,
  lowBalanceThreshold: 100,
  syncFailureAlerts: true,
  watchlistAnalystAlerts: false,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
} as unknown as UserProfile;

function create() {
  const toast = {show: vi.fn()};
  const store = {
    profile: signal<UserProfile | null>(profile),
    saveProfile: vi.fn(),
  };
  const appearance = {seed: signal<string | null>(null), intensity: signal(0.12), choose: vi.fn()};
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: AuthStore, useValue: {canManageUsers: signal(false)}},
      {provide: ToastService, useValue: toast},
    ],
  });
  TestBed.overrideComponent(SettingsComponent, {
    set: {
      providers: [
        {provide: SettingsStore, useValue: store},
        {provide: PushStore, useValue: {}},
        {provide: AppearanceStore, useValue: appearance},
      ],
      template: '',
    },
  });
  const fixture = TestBed.createComponent(SettingsComponent);
  return {store, toast, appearance, fixture, component: fixture.componentInstance};
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
    form.controls.realAnnualReturn.setValue('6');
    component.saveProfile();
    expect(store.saveProfile).toHaveBeenCalledWith(
      expect.objectContaining({safeWithdrawalRate: 0.035, realAnnualReturn: 0.06})
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
    component.saveProfile();
    expect(store.saveProfile).not.toHaveBeenCalled();
    expect(form.controls.safeWithdrawalRate.touched).toBe(true);
    form.controls.safeWithdrawalRate.setValue('4');
    component.saveProfile();
    expect(store.saveProfile).toHaveBeenCalledTimes(1);
  });

  it('does not toast from the component on dispatch', () => {
    const {component, toast} = setup();
    component.saveProfile();
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

describe('SettingsComponent Save Profile button', () => {
  function render() {
    const store = {
      profile: signal<UserProfile | null>(profile),
      avatarInitials: signal('AL'),
      fullName: signal('Ada Lovelace'),
      profileSaving: signal(false),
      updateProfile: vi.fn(),
      saveProfile: vi.fn(),
    };
    const push = {
      pushEnabled: signal(false),
      canToggle: signal(false),
      serverNotice: signal(''),
      browserNotice: signal(''),
      loadErrorMessage: signal(''),
      actionErrorMessage: signal(''),
      rows: signal([]),
      isBusy: signal(false),
      removingId: signal<string | null>(null),
      togglePush: vi.fn(),
      remove: vi.fn(),
    };
    const appearance = {
      seed: signal<string | null>(null),
      intensity: signal(0.12),
      choose: vi.fn(),
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
          {provide: AppearanceStore, useValue: appearance},
        ],
      },
    });
    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const saveButton = (): HTMLButtonElement => {
      const button = Array.from(el.querySelectorAll('button')).find(b =>
        b.textContent?.includes('Save Profile')
      );
      if (!button) {
        throw new Error('Save Profile button missing');
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

describe('SettingsComponent appearance', () => {
  it('hands a picked colour to the appearance store', () => {
    const {appearance, component} = create();
    const detail = {seed: '#4f46e5', intensity: 0.3};
    component.onThemeChoice(new CustomEvent('lk-theme-picker-change', {detail}));
    expect(appearance.choose).toHaveBeenCalledWith(detail);
  });

  it('hands a reset to the appearance store as a null seed', () => {
    const {appearance, component} = create();
    const detail = {seed: null, intensity: 0.12};
    component.onThemeChoice(new CustomEvent('lk-theme-picker-change', {detail}));
    expect(appearance.choose).toHaveBeenCalledWith(detail);
  });

  it('offers Petrol as the app default', () => {
    expect(create().component.appSeed).toBe('#175a6d');
  });
});
