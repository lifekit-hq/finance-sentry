import {DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {Router} from '@angular/router';
import {
  AlertComponent,
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
import {HoldingsStore} from '../../store/holdings.store';

const SKELETON_ROWS = 4;

@Component({
  selector: 'fns-investments',
  imports: [
    AlertComponent,
    AssetLogoPipe,
    CardComponent,
    CmnCellDirective,
    CmnColumnComponent,
    DataTableComponent,
    DecimalPipe,
    MoneyPipe,
    SkeletonComponent,
    DonutChartComponent,
    InstitutionAvatarComponent,
  ],
  templateUrl: './holdings.component.html',
  styles: `
    @media (min-width: 768px) {
      :host ::ng-deep .holdings-table table {
        table-layout: fixed;
      }
      :host ::ng-deep .holdings-table .cdk-column-quantity {
        width: 8rem;
      }
      :host ::ng-deep .holdings-table .cdk-column-price,
      :host ::ng-deep .holdings-table .cdk-column-value {
        width: 7rem;
      }
      :host ::ng-deep .holdings-table .cdk-column-pnl {
        width: 11rem;
      }
      :host ::ng-deep .holdings-table .cdk-column-weight {
        width: 5rem;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [HoldingsStore],
})
export class InvestmentsComponent {
  private readonly router = inject(Router);
  public readonly store = inject(HoldingsStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});
  public readonly pnlPositiveClass = 'text-status-success';
  public readonly pnlNegativeClass = 'text-status-error';

  public navigateToDossier(symbol: string): void {
    void this.router.navigate([AppRoute.AssetDossier, symbol]);
  }
}
