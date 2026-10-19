import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type BookPerformance} from '../../models/book-performance/book-performance.model';
import {type BookPerformanceState} from './book-performance.state';

export function bookPerformanceMethods(store: WritableStateSource<BookPerformanceState>) {
  return {
    setPerformance(performance: BookPerformance): void {
      patchState(store, {performance});
    },
  };
}
