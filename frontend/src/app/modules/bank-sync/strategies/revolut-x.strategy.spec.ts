import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {RevolutXService} from '../services/revolut-x.service';
import {RevolutXConnectStrategy} from './revolut-x.strategy';

describe('RevolutXConnectStrategy', () => {
  let strategy: RevolutXConnectStrategy;
  let revolutX: {connect: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    revolutX = {connect: vi.fn()};
    TestBed.configureTestingModule({
      providers: [{provide: RevolutXService, useValue: revolutX}, RevolutXConnectStrategy],
    });
    strategy = TestBed.inject(RevolutXConnectStrategy);
  });

  it('forwards the key pair to RevolutXService.connect', () => {
    revolutX.connect.mockReturnValue(of({message: 'ok', holdingsCount: 2, syncedAt: ''}));
    const payload = {apiKey: 'k', privateKey: 'pem'};
    strategy.submit(payload).subscribe();
    expect(revolutX.connect).toHaveBeenCalledWith(payload);
  });

  it('maps the response to a crypto outcome', () => {
    revolutX.connect.mockReturnValue(of({message: 'ok', holdingsCount: 2, syncedAt: ''}));
    let outcome: unknown;
    strategy.submit({apiKey: 'k', privateKey: 'pem'}).subscribe(o => (outcome = o));
    expect(outcome).toEqual({successCode: 'POLLING', count: 1, institutionType: 'crypto'});
  });

  it('exposes slug "revolut_x"', () => {
    expect(strategy.slug).toBe('revolut_x');
    expect(strategy.formComponent).toBeDefined();
  });
});
