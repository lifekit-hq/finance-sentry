import {type BookPerformance} from '../../models/book-performance/book-performance.model';

export interface BookPerformanceState {
  performance: Nullable<BookPerformance>;
}

export const initialBookPerformanceState: BookPerformanceState = {
  performance: null,
};
