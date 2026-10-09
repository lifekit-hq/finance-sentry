import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {InzhurService} from '../services/inzhur.service';
import {InzhurConnectStrategy} from './inzhur.strategy';

// Placeholder value only; no real cookie belongs in a test.
const PASTED = {refreshToken: 'fake-refresh'};

describe('InzhurConnectStrategy', () => {
  let strategy: InzhurConnectStrategy;
  let inzhur: {connectSession: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    inzhur = {connectSession: vi.fn()};
    TestBed.configureTestingModule({
      providers: [{provide: InzhurService, useValue: inzhur}, InzhurConnectStrategy],
    });
    strategy = TestBed.inject(InzhurConnectStrategy);
  });

  it('hands over the pasted session and reports a broker/CONNECTED outcome', () => {
    inzhur.connectSession.mockReturnValue(of({status: 'connected'}));
    let outcome: unknown;

    strategy.submit(PASTED).subscribe(o => (outcome = o));

    expect(inzhur.connectSession).toHaveBeenCalledWith(PASTED);
    expect(outcome).toEqual({successCode: 'CONNECTED', count: 0, institutionType: 'broker'});
  });

  it('passes a refused session through as the backend error', () => {
    const refused = {error: {errorCode: 'INZHUR_SESSION_REJECTED'}};
    inzhur.connectSession.mockReturnValue(throwError(() => refused));
    let error: unknown;

    strategy.submit(PASTED).subscribe({error: (e: unknown) => (error = e)});

    expect(error).toBe(refused);
  });
});
