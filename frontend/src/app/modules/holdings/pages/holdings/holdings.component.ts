import {DecimalPipe, NgTemplateOutlet} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {
  AsyncStateComponent,
  type AsyncStateStatus,
  CardComponent,
  CmnCellDirective,
  CmnColumnComponent,
  DataTableComponent,
  DonutChartComponent,
  InstitutionAvatarComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectStore} from '../../../bank-sync/store/connect/connect.store';
import {HoldingsStore} from '../../store/holdings.store';

const SKELETON_ROWS = 4;

@Component({
  selector: 'fns-investments',
  imports: [
    AssetLogoPipe,
    AsyncStateComponent,
    CardComponent,
    CmnCellDirective,
    CmnColumnComponent,
    DataTableComponent,
    DecimalPipe,
    MoneyPipe,
    NgTemplateOutlet,
    RouterLink,
    SkeletonComponent,
    DonutChartComponent,
    InstitutionAvatarComponent,
  ],
  templateUrl: './holdings.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [HoldingsStore],
})
export class InvestmentsComponent {
  private readonly router = inject(Router);
  public readonly store = inject(HoldingsStore);
  public readonly connectStore = inject(ConnectStore);
  public readonly accountsRoute = AppRoute.AccountsInvestments;
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});
  public readonly positionsStatus = computed<AsyncStateStatus>(() => {
    if (this.store.positionsErrorMessage()) {
      return 'error';
    }
    return this.store.isPositionsLoading() ? 'loading' : 'success';
  });
  public readonly pnlPositiveClass = 'text-status-success';
  public readonly pnlNegativeClass = 'text-status-error';

  public navigateToDossier(symbol: string): void {
    void this.router.navigate([AppRoute.AssetDossier, symbol]);
  }
}
