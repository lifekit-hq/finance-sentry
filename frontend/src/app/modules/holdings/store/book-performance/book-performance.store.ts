import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {bookPerformanceComputed} from './book-performance.computed';
import {bookPerformanceEffects, bookPerformanceHooks} from './book-performance.effects';
import {bookPerformanceMethods} from './book-performance.methods';
import {initialBookPerformanceState} from './book-performance.state';

/** Component-scoped: the investments tab's growth-of-10,000 card. */
export const BookPerformanceStore = signalStore(
  withState(initialBookPerformanceState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the book against SPY.'}),
  withMethods(bookPerformanceMethods),
  withComputed(bookPerformanceComputed),
  withMethods(bookPerformanceEffects),
  withHooks(bookPerformanceHooks)
);
