import {Pipe, type PipeTransform} from '@angular/core';

import {type SyncStatus} from '../models/wealth/wealth.model';

type TagVariant = 'success' | 'warning' | 'error' | 'neutral';

const VARIANT_MAP: Record<SyncStatus, TagVariant> = {
  synced: 'success',
  pending: 'warning',
  syncing: 'warning',
  stale: 'warning',
  failed: 'error',
  // eslint-disable-next-line @typescript-eslint/naming-convention
  reauth_required: 'error',
  retrying: 'warning',
  // eslint-disable-next-line @typescript-eslint/naming-convention
  reconnect_needed: 'error',
  dormant: 'neutral',
};

@Pipe({name: 'syncStatusVariant'})
export class SyncStatusVariantPipe implements PipeTransform {
  public transform(syncStatus: SyncStatus): TagVariant {
    return VARIANT_MAP[syncStatus];
  }
}
