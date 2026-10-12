import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type InzhurConnectionStatus} from '../../models/inzhur/inzhur.model';
import {InzhurService} from '../../services/inzhur.service';
import {InzhurConnectStore} from './inzhur-connect.store';

const ENDED: InzhurConnectionStatus = {
  status: 'reauth_required',
  lastSyncAt: '2026-10-06T07:00:00Z',
  sessionStartedAt: '2026-10-01T07:00:00Z',
};
const NEW: InzhurConnectionStatus = {
  status: 'not_connected',
  lastSyncAt: null,
  sessionStartedAt: null,
};

describe('InzhurConnectStore', () => {
  let inzhur: {getStatus: ReturnType<typeof vi.fn>};

  beforeEach(() => {
    inzhur = {getStatus: vi.fn().mockReturnValue(of(NEW))};
    TestBed.configureTestingModule({
      providers: [InzhurConnectStore, {provide: InzhurService, useValue: inzhur}],
    });
  });

  it('loads the connection on init', () => {
    const store = TestBed.inject(InzhurConnectStore);

    expect(inzhur.getStatus).toHaveBeenCalledTimes(1);
    expect(store.asyncStatus()).toBe('success');
    expect(store.isReconnect()).toBe(false);
  });

  it('flags a reconnect when the saved session ended', () => {
    inzhur.getStatus.mockReturnValue(of(ENDED));
    const store = TestBed.inject(InzhurConnectStore);

    expect(store.isReconnect()).toBe(true);
  });

  it('reports a failed status load as an error state', () => {
    inzhur.getStatus.mockReturnValue(throwError(() => new Error('down')));
    const store = TestBed.inject(InzhurConnectStore);

    expect(store.asyncStatus()).toBe('error');
    expect(store.loadErrorMessage()).toContain("Couldn't load your Inzhur connection");
  });
});
