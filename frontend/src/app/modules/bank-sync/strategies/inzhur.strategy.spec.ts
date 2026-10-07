import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {INZHUR_INVALID_CODE} from '../constants/inzhur/inzhur.constants';
import {InzhurService} from '../services/inzhur.service';
import {InzhurConnectStrategy} from './inzhur.strategy';

describe('InzhurConnectStrategy', () => {
  let strategy: InzhurConnectStrategy;
  let inzhur: {verifyLogin: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    inzhur = {verifyLogin: vi.fn()};
    TestBed.configureTestingModule({
      providers: [{provide: InzhurService, useValue: inzhur}, InzhurConnectStrategy],
    });
    strategy = TestBed.inject(InzhurConnectStrategy);
  });

  it('submits the SMS code and reports a broker/CONNECTED outcome', () => {
    inzhur.verifyLogin.mockReturnValue(
      of({status: 'connected', codeExpiresAt: null, attemptsLeft: null})
    );
    let outcome: unknown;

    strategy.submit({code: '000000'}).subscribe(o => (outcome = o));

    expect(inzhur.verifyLogin).toHaveBeenCalledWith({code: '000000'});
    expect(outcome).toEqual({successCode: 'CONNECTED', count: 0, institutionType: 'broker'});
  });

  it('raises a wrong code as INZHUR_INVALID_CODE with the attempts left', () => {
    inzhur.verifyLogin.mockReturnValue(
      of({status: 'invalid_code', codeExpiresAt: null, attemptsLeft: 2})
    );
    let error: unknown;

    strategy.submit({code: '000000'}).subscribe({error: (e: unknown) => (error = e)});

    expect(error).toEqual({errorCode: INZHUR_INVALID_CODE, attemptsLeft: 2});
  });
});
