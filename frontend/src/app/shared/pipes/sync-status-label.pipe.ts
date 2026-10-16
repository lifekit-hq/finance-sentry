import {Pipe, type PipeTransform} from '@angular/core';

import {type SyncStatus} from '../models/wealth/wealth.model';

const LABEL_MAP: Record<SyncStatus, string> = {
  synced: 'Synced',
  pending: 'Pending',
  syncing: 'Syncing',
  stale: 'Stale',
  failed: 'Failed',
  // eslint-disable-next-line @typescript-eslint/naming-convention
  reauth_required: 'Reauth',
  retrying: 'Retrying',
  // eslint-disable-next-line @typescript-eslint/naming-convention
  reconnect_needed: 'Reconnect needed',
  dormant: 'Dormant',
};

@Pipe({name: 'syncStatusLabel'})
export class SyncStatusLabelPipe implements PipeTransform {
  public transform(syncStatus: SyncStatus): string {
    return LABEL_MAP[syncStatus];
  }
}
