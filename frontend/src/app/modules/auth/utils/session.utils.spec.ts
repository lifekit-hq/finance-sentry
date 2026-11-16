import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it} from 'vitest';

import {SessionUtils} from './session.utils';

describe('SessionUtils.afterSession', () => {
  it('decides at once when the session already answered', () => {
    const results: string[] = [];
    TestBed.runInInjectionContext(() =>
      SessionUtils.afterSession(signal(true), () => 'now').subscribe(v => results.push(v))
    );
    TestBed.tick();

    expect(results).toEqual(['now']);
  });

  it('holds the decision until the session answers, then decides once', () => {
    const resolved = signal(false);
    const results: string[] = [];
    TestBed.runInInjectionContext(() =>
      SessionUtils.afterSession(resolved, () => 'later').subscribe(v => results.push(v))
    );
    TestBed.tick();

    expect(results).toEqual([]);

    resolved.set(true);
    TestBed.tick();

    expect(results).toEqual(['later']);
  });
});
