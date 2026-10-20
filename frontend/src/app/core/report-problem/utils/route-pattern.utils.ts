import {type Nullable} from '@lifekit-hq/ui';

import {UNKNOWN_ROUTE_PATTERN} from '../constants/report-problem.constants';

/** The part of an `ActivatedRouteSnapshot` the pattern is built from. */
export interface RouteSnapshotLike {
  routeConfig: Nullable<{path?: string}>;
  firstChild: Nullable<RouteSnapshotLike>;
}

export class RoutePatternUtils {
  /**
   * The router-config pattern of the active route (`/accounts/:id`), joined from each level's configured
   * `path`. Real URL segments, the query string and the fragment never enter: only config paths are read.
   */
  public static fromSnapshot(root: Nullable<RouteSnapshotLike>): string {
    const segments: string[] = [];
    for (let route = root; route; route = route.firstChild) {
      const path = route.routeConfig?.path;
      if (path) {
        segments.push(path);
      }
    }
    return segments.length === 0 ? UNKNOWN_ROUTE_PATTERN : `/${segments.join('/')}`;
  }
}
