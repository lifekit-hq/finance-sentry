import {type Nullable} from '@lifekit-hq/ui';

import {type BookPerformance} from '../../models/book-performance/book-performance.model';
import {type TrackRecord} from '../../models/book-performance/track-record.model';

export interface BookPerformanceState {
  performance: Nullable<BookPerformance>;
  trackRecord: Nullable<TrackRecord>;
}

export const initialBookPerformanceState: BookPerformanceState = {
  performance: null,
  trackRecord: null,
};
