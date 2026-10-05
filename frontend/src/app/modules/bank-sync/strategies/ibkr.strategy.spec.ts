import {TestBed} from '@angular/core/testing';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {IBKRService} from '../services/ibkr.service';
import {IbkrConnectStrategy} from './ibkr.strategy';

describe('IbkrConnectStrategy', () => {
  let strategy: IbkrConnectStrategy;
  let ibkr: {connect: ReturnType<typeof vi.fn>; connectFlex: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    ibkr = {connect: vi.fn(), connectFlex: vi.fn()};
    TestBed.configureTestingModule({
      providers: [{provide: IBKRService, useValue: ibkr}, IbkrConnectStrategy],
    });
    strategy = TestBed.inject(IbkrConnectStrategy);
  });

  const oauth = {
    consumerKey: 'FINSENTRY',
    accessToken: 'a',
    accessTokenSecret: 's',
    signatureKey: 'sig',
    encryptionKey: 'enc',
    dhParam: 'dh',
  };

  it('forwards the OAuth payload to IBKRService.connect', () => {
    ibkr.connect.mockReturnValue(of({holdingsCount: 4, accountId: 'DU123', connectedAt: 'now'}));

    strategy.submit({kind: 'oauth', payload: oauth}).subscribe();

    expect(ibkr.connect).toHaveBeenCalledWith(oauth);
    expect(ibkr.connectFlex).not.toHaveBeenCalled();
  });

  it('maps the OAuth holdingsCount into a broker/CONNECTED outcome', () => {
    ibkr.connect.mockReturnValue(of({holdingsCount: 4, accountId: 'DU123', connectedAt: 'now'}));
    let outcome: unknown;
    strategy.submit({kind: 'oauth', payload: oauth}).subscribe(o => (outcome = o));
    expect(outcome).toEqual({successCode: 'CONNECTED', count: 4, institutionType: 'broker'});
  });

  it('forwards the Flex pair to IBKRService.connectFlex and reports a broker/CONNECTED outcome', () => {
    ibkr.connectFlex.mockReturnValue(of(undefined));
    const payload = {token: 'tok', queryId: '123456'};
    let outcome: unknown;

    strategy.submit({kind: 'flex', payload}).subscribe(o => (outcome = o));

    expect(ibkr.connectFlex).toHaveBeenCalledWith(payload);
    expect(ibkr.connect).not.toHaveBeenCalled();
    expect(outcome).toEqual({
      successCode: 'CONNECTED',
      count: 0,
      institutionType: 'broker',
      importPending: true,
    });
  });

  it('exposes slug "ibkr"', () => {
    expect(strategy.slug).toBe('ibkr');
    expect(strategy.formComponent).toBeDefined();
  });
});
