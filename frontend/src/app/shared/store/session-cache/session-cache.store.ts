import {signalStore, withMethods, withState} from '@ngrx/signals';

import {sessionCacheMethods} from './session-cache.methods';
import {initialSessionCacheState} from './session-cache.state';

/**
 * Keeps a page store's last data for the length of the session, so a revisit paints it at once
 * while the page revalidates (the phone contract's continuity rule). Memory only, by design: no
 * financial data is stored on the device. See `withSessionCache`.
 */
export const SessionCacheStore = signalStore(
  {providedIn: 'root'},
  withState(initialSessionCacheState),
  withMethods(sessionCacheMethods)
);
