import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../../modules/auth/store/auth.store';
import {OWNER_ROLE} from '../../../../shared/constants/role/role.constants';
import {type WhatsNewData} from '../../models/whats-new.model';
import {WhatsNewService} from '../../services/whats-new.service';
import {WhatsNewStore} from '../../store/whats-new.store';
import {WhatsNewPanelComponent} from './whats-new-panel.component';

const DATA: WhatsNewData = {
  versions: [
    {
      version: '9.9.0',
      date: '2026-10-05',
      notes: [
        {text: 'You can do a thing.', owner: false},
        {text: 'You can invite people.', owner: true},
      ],
      changes: ['Show a thing', 'Fix a thing'],
    },
    {version: '9.8.0', date: '2026-09-29', notes: [], changes: ['Older change']},
  ],
};

describe('WhatsNewPanelComponent', () => {
  const load = vi.fn();

  const render = (roles: string[] = []): HTMLElement => {
    TestBed.configureTestingModule({
      providers: [
        {provide: WhatsNewService, useValue: {load}},
        {provide: AuthStore, useValue: {roles: signal(roles)}},
      ],
    });
    TestBed.inject(WhatsNewStore).load();
    const fixture = TestBed.createComponent(WhatsNewPanelComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    load.mockReset();
    load.mockReturnValue(of(DATA));
  });

  it('lists the newest version first, with its date and plain lines', () => {
    const host = render();
    const versions = host.querySelectorAll('fns-whats-new-version h2');

    expect(Array.from(versions).map(h => h.textContent?.trim())).toEqual(['9.9.0', '9.8.0']);
    expect(host.querySelector('time')?.textContent).toContain('Oct 5, 2026');
    expect(host.querySelector('[data-testid="whats-new-note"]')?.textContent).toContain(
      'You can do a thing.'
    );
  });

  it('keeps every change under a closed fold when the version has notes', () => {
    const fold = render().querySelector<HTMLDetailsElement>(
      '[data-testid="whats-new-9.9.0"] details'
    );

    expect(fold?.open).toBe(false);
    expect(fold?.textContent).toContain('Every change in this version');
    expect(fold?.textContent).toContain('Fix a thing');
  });

  it('opens the filtered list for a version with no plain notes', () => {
    const fold = render().querySelector<HTMLDetailsElement>(
      '[data-testid="whats-new-9.8.0"] details'
    );

    expect(fold?.open).toBe(true);
    expect(fold?.textContent).toContain('Older change');
  });

  it('hides owner lines from a Member and shows them to the Owner', () => {
    const noteTexts = (host: HTMLElement): string[] =>
      Array.from(host.querySelectorAll('[data-testid="whats-new-note"]')).map(
        li => li.textContent?.trim() ?? ''
      );

    expect(noteTexts(render())).toEqual(['You can do a thing.']);

    TestBed.resetTestingModule();
    expect(noteTexts(render([OWNER_ROLE]))).toEqual([
      'You can do a thing.',
      'You can invite people.',
    ]);
  });
});
