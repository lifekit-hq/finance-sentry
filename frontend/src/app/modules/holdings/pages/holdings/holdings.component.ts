import {DecimalPipe, NgTemplateOutlet} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {type DonutSegmentClick} from '@lifekit-hq/charts-core';
import {
  AsyncStateComponent,
  type AsyncStateStatus,
  CardComponent,
  CmnCellDirective,
  CmnColumnComponent,
  DataTableComponent,
  DisclosureRowComponent,
  DonutChartComponent,
  InstitutionAvatarComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectStore} from '../../../bank-sync/store/connect/connect.store';
import {BrokerageGrowthComponent} from '../../components/brokerage-growth/brokerage-growth.component';
import {type AssetClass, type PositionRow} from '../../store/holdings.computed';
import {HoldingsStore} from '../../store/holdings.store';

const SKELETON_ROWS = 4;

@Component({
  selector: 'fns-investments',
  imports: [
    AssetLogoPipe,
    AsyncStateComponent,
    BrokerageGrowthComponent,
    CardComponent,
    CmnCellDirective,
    CmnColumnComponent,
    DataTableComponent,
    DecimalPipe,
    DisclosureRowComponent,
    HeadlineBadgeComponent,
    MoneyPipe,
    NgTemplateOutlet,
    RouterLink,
    SkeletonComponent,
    DonutChartComponent,
    InstitutionAvatarComponent,
  ],
  templateUrl: './holdings.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [HoldingsStore],
})
export class InvestmentsComponent {
  private readonly router = inject(Router);
  public readonly store = inject(HoldingsStore);
  public readonly connectStore = inject(ConnectStore);
  public readonly accountsRoute = AppRoute.AccountsInvestments;
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});
  public readonly positionsStatus = computed<AsyncStateStatus>(() => {
    if (this.store.errorMessage()) {
      return 'error';
    }
    return this.store.isLoading() ? 'loading' : 'success';
  });
  public readonly pnlPositiveClass = GAIN_TEXT_CLASS;
  public readonly pnlNegativeClass = LOSS_TEXT_CLASS;

  /** The shared position-table template receives its rows untyped; this gives the table its row type back. */
  public asRows(rows: unknown): PositionRow[] {
    return rows as PositionRow[];
  }

  /** Removes the provider filter, which drops `?provider=` from the URL. */
  public clearProvider(): void {
    void this.router.navigate([], {queryParams: {provider: null}, queryParamsHandling: 'merge'});
  }

  /** Cash rows are currency balances, not holdings - they have no dossier to open. */
  public openDossier(assetClass: AssetClass, symbol: string): void {
    if (assetClass === 'cash') {
      return;
    }
    void this.router.navigate([AppRoute.AssetDossier, symbol]);
  }

  /**
   * A slice is an asset class, which has no page of its own; the one case with an entity behind it
   * is a class holding a single position, which opens that position's dossier. Cash and classes
   * with several positions open nothing.
   */
  public openSegment(click: DonutSegmentClick): void {
    const group = this.store.positionsByAssetClass()[click.index];
    const positions = group ? [...group.rows, ...group.hiddenRows] : [];
    if (group && positions.length === 1) {
      this.openDossier(group.assetClass, positions[0].symbol);
    }
  }
}
