import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {API_BASE_URL} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {PeopleService} from '../../services/people.service';
import {PeopleComponent} from './people.component';

describe('PeopleComponent', () => {
  let fixture: ComponentFixture<PeopleComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PeopleComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: 'http://api.test'},
        {provide: PeopleService, useValue: {list: vi.fn()}},
      ],
    });
    fixture = TestBed.createComponent(PeopleComponent);
  });

  it('renders a back link that navigates to Settings', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
    const back = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="people-back"]'
    );
    expect(back).not.toBeNull();
    expect(back?.textContent).toContain('Settings');
    fixture.componentInstance.goBack();
    expect(navigate).toHaveBeenCalledWith([AppRoute.Settings]);
  });
});
