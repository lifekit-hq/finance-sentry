import {type Signal} from '@angular/core';
import {toObservable} from '@angular/core/rxjs-interop';
import {filter, map, type Observable, take} from 'rxjs';

export class SessionUtils {
  /** Runs `decide` once the first session call has answered; at once when it already has. */
  public static afterSession<T>(sessionResolved: Signal<boolean>, decide: () => T): Observable<T> {
    return toObservable(sessionResolved).pipe(
      filter(Boolean),
      take(1),
      map(() => decide())
    );
  }
}
