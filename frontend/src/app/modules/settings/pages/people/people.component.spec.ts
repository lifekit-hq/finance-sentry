import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {API_BASE_URL} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {PeopleService} from '../../services/people.service';
import {PeopleComponent} from './people.component';

describe('PeopleComponent', () => {
  let fixture: ComponentFixture<PeopleComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PeopleComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
        {provide: PeopleService, useValue: {list: vi.fn()}},
      ],
    });
    fixture = TestBed.createComponent(PeopleComponent);
  });

  it('leaves the title and the way back to the top bar', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="people-back"]')).toBeNull();
    expect(host.querySelector('h1')).toBeNull();
  });
});
