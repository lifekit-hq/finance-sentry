import {type SyncStatus} from '../models/wealth/wealth.model';
import {SyncStatusLabelPipe} from './sync-status-label.pipe';
import {SyncStatusVariantPipe} from './sync-status-variant.pipe';

describe('SyncStatusLabelPipe and SyncStatusVariantPipe', () => {
  const label = new SyncStatusLabelPipe();
  const variant = new SyncStatusVariantPipe();

  it.each<[SyncStatus, string, string]>([
    ['synced', 'Synced', 'success'],
    ['pending', 'Pending', 'warning'],
    ['syncing', 'Syncing', 'warning'],
    ['stale', 'Stale', 'warning'],
    ['failed', 'Failed', 'error'],
    ['reauth_required', 'Reauth', 'error'],
    ['retrying', 'Retrying', 'warning'],
    ['reconnect_needed', 'Reconnect needed', 'error'],
    ['dormant', 'Dormant', 'neutral'],
  ])('labels %s "%s" in the %s colour', (status, text, colour) => {
    expect(label.transform(status)).toBe(text);
    expect(variant.transform(status)).toBe(colour);
  });
});
