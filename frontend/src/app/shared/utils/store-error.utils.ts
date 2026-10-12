import {extractErrorCode} from '@lifekit-hq/core';
import {catchError, EMPTY, type MonoTypeOperatorFunction} from 'rxjs';

interface ErrorSink {
  setError(code: Nullable<string>): void;
}

export class StoreErrorUtils {
  public static catchAndSetError<T>(sink: ErrorSink): MonoTypeOperatorFunction<T> {
    return catchError<T, typeof EMPTY>((err: unknown) => {
      sink.setError(extractErrorCode(err));
      return EMPTY;
    });
  }
}
