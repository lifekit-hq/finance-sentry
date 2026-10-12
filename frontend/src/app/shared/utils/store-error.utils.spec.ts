import {HttpErrorResponse} from '@angular/common/http';
import {of, throwError} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {StoreErrorUtils} from './store-error.utils';

describe('StoreErrorUtils', () => {
  it('forwards values when no error', () => {
    const sink = {setError: vi.fn()};
    const next = vi.fn();
    of(1, 2, 3).pipe(StoreErrorUtils.catchAndSetError(sink)).subscribe(next);

    expect(next).toHaveBeenCalledTimes(3);
    expect(sink.setError).not.toHaveBeenCalled();
  });

  it('extracts error code, calls setError, swallows error', () => {
    const sink = {setError: vi.fn()};
    const apiErr = new HttpErrorResponse({status: 400, error: {errorCode: 'X_ERR'}});

    let completed = false;
    throwError(() => apiErr)
      .pipe(StoreErrorUtils.catchAndSetError(sink))
      .subscribe({complete: () => (completed = true)});

    expect(sink.setError).toHaveBeenCalledWith('X_ERR');
    expect(completed).toBe(true);
  });

  it('passes null when error has no code', () => {
    const sink = {setError: vi.fn()};
    throwError(() => new Error('no code'))
      .pipe(StoreErrorUtils.catchAndSetError(sink))
      .subscribe();

    expect(sink.setError).toHaveBeenCalledWith(null);
  });
});
