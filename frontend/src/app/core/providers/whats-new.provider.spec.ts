import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {CMN_WHATS_NEW_CONFIG, type WhatsNewConfig} from '@lifekit-hq/ui';

import {AuthStore} from '../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../shared/constants/version/version.constants';
import {provideAppWhatsNew, WHATS_NEW_STORAGE_KEY} from './whats-new.provider';

describe('provideAppWhatsNew', () => {
  const roles = signal<string[]>([]);

  const config = (): WhatsNewConfig => {
    TestBed.configureTestingModule({
      providers: [{provide: AuthStore, useValue: {roles}}, provideAppWhatsNew()],
    });
    return TestBed.inject(CMN_WHATS_NEW_CONFIG);
  };

  it('gives the running version and keeps the storage key devices already hold', () => {
    const {version, storageKey} = config();

    expect(version).toBe(APP_VERSION);
    expect(storageKey).toBe('fns.whatsNew.lastSeen');
    expect(WHATS_NEW_STORAGE_KEY).toBe(storageKey);
  });

  it('includes the owner notes only for the Owner role, as the roles change', () => {
    const {includeOwnerNotes} = config();

    roles.set(['Member']);
    expect(includeOwnerNotes?.()).toBe(false);

    roles.set(['Owner']);
    expect(includeOwnerNotes?.()).toBe(true);
  });
});
