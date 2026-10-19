import {DecimalPipe} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {RouterLink} from '@angular/router';
import {
  AsyncStateComponent,
  type AsyncStateStatus,
  CardComponent,
  DonutChartComponent,
  InstitutionAvatarComponent,
  ListItemRowComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {HoldingsStore} from '../../store/holdings.store';
import {BrokerageGrowthComponent} from '../brokerage-growth/brokerage-growth.component';

const SKELETON_ROWS = 5;

/**
 * Home's Investments card: the invested total with its quality badge, the allocation by asset
 * class, the brokerage sleeve's contributions against growth, and the largest holdings, each
 * opening its dossier. Renders nothing for a book with no positions and no failed feed.
 */
@Component({
  selector: 'fns-investments-card',
  imports: [
    AssetLogoPipe,
    AsyncStateComponent,
    BrokerageGrowthComponent,
    CardComponent,
    DecimalPipe,
    DonutChartComponent,
    HeadlineBadgeComponent,
    InstitutionAvatarComponent,
    ListItemRowComponent,
    MoneyPipe,
    RouterLink,
    SkeletonComponent,
  ],
  templateUrl: './investments-card.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [HoldingsStore],
})
export class InvestmentsCardComponent {
  public readonly store = inject(HoldingsStore);
  public readonly dossierRoute = AppRoute.AssetDossier;
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});
  public readonly status = computed<AsyncStateStatus>(() => {
    if (this.store.errorMessage()) {
      return 'error';
    }
    return this.store.isLoading() ? 'loading' : 'success';
  });
  /** A settled book with nothing in it has no card; loading and failed feeds keep theirs. */
  public readonly isHidden = computed(() => this.status() === 'success' && this.store.isEmpty());
}
