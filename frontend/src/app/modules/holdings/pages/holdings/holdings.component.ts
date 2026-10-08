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

import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectStore} from '../../../bank-sync/store/connect/connect.store';
import {type AssetClass} from '../../store/holdings.computed';
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
  public readonly pnlPositiveClass = GAIN_TEXT_CLASS;
  public readonly pnlNegativeClass = LOSS_TEXT_CLASS;

  /** Cash rows are currency balances, not holdings - they have no dossier to open. */
  public openDossier(assetClass: AssetClass, symbol: string): void {
    if (assetClass === 'cash') {
      return;
    }
    void this.router.navigate([AppRoute.AssetDossier, symbol]);
  }
}
