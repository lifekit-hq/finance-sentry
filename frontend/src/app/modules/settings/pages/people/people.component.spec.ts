import {HttpErrorResponse, provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {API_BASE_URL} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {type Person} from '../../models/person/person.model';
import {PeopleService} from '../../services/people.service';
import {PeopleStore} from '../../store/people/people.store';
import {PeopleComponent} from './people.component';

const PEOPLE: Person[] = [
  {id: 'u-1', email: 'friend@test.com', roles: ['Member'], status: 'Invited'},
];

function retryButton(host: HTMLElement): Nullable<HTMLButtonElement> {
  return (
    Array.from(host.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Retry') ?? null
  );
}

describe('PeopleComponent', () => {
  let fixture: ComponentFixture<PeopleComponent>;
  let service: {list: ReturnType<typeof vi.fn>};

  function render(): HTMLElement {
    fixture = TestBed.createComponent(PeopleComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    service = {list: vi.fn().mockReturnValue(of(PEOPLE))};
    TestBed.configureTestingModule({
      imports: [PeopleComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
        {provide: PeopleService, useValue: service},
      ],
    });
  });

  it('leaves the title and the way back to the top bar', () => {
    const host = render();
    expect(host.querySelector('[data-testid="people-back"]')).toBeNull();
    expect(host.querySelector('h1')).toBeNull();
  });

  it('lists the people once loaded', () => {
    const host = render();

    expect(host.textContent).toContain('friend@test.com');
    expect(host.textContent).not.toContain('No one here yet');
  });

  it('shows a skeleton and no rows while the first load is in flight', () => {
    service.list.mockReturnValue(NEVER);

    const host = render();

    expect(host.querySelector('cmn-skeleton')).not.toBeNull();
    expect(host.textContent).not.toContain('friend@test.com');
    expect(host.textContent).not.toContain('No one here yet');
  });

  it('shows the call to action when nobody is on the account yet', () => {
    service.list.mockReturnValue(of([]));

    const host = render();

    expect(host.textContent).toContain('No one here yet');
    expect(host.textContent).toContain('Invite someone to get started.');
  });

  it('shows the load error, never an empty state, and reloads on Retry', () => {
    service.list.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'BOOM'}}))
    );
    const host = render();

    expect(host.textContent).toContain('Failed to load people.');
    expect(host.textContent).not.toContain('No one here yet');
    expect(service.list).toHaveBeenCalledTimes(1);

    service.list.mockReturnValue(of(PEOPLE));
    retryButton(host)?.click();
    fixture.detectChanges();

    expect(service.list).toHaveBeenCalledTimes(2);
    expect(host.textContent).toContain('friend@test.com');
    expect(host.textContent).not.toContain('Failed to load people.');
  });

  it('keeps the rows under the load error when a reload fails while offline', () => {
    TestBed.overrideProvider(ConnectivityService, {useValue: {offline: signal(true)}});
    const host = render();
    expect(host.textContent).toContain('friend@test.com');

    service.list.mockReturnValue(
      throwError(() => new HttpErrorResponse({status: 500, error: {errorCode: 'BOOM'}}))
    );
    fixture.debugElement.injector.get(PeopleStore).load();
    fixture.detectChanges();

    expect(host.textContent).toContain('Failed to load people.');
    expect(host.textContent).toContain('friend@test.com');
  });

  it('offers Reopen, not Revoke, for a closed account and names when its data is erased', () => {
    service.list.mockReturnValue(
      of([
        {
          id: 'u-2',
          email: 'closed@test.com',
          roles: ['Member'],
          status: 'Closed',
          purgeDueAt: '2026-11-13T09:00:00Z',
        },
      ])
    );

    const host = render();
    const labels = Array.from(host.querySelectorAll('button')).map(b => b.textContent?.trim());

    expect(labels).toContain('Reopen');
    expect(labels).not.toContain('Revoke');
    expect(host.textContent).toContain('erased Fri 13 Nov');
  });
});
