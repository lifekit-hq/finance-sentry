import {DatePipe, DecimalPipe, formatDate} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {RouterLink} from '@angular/router';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  type ChartDomain,
  type ChartPoint,
  CmnCellDirective,
  CmnColumnComponent,
  DataTableComponent,
  EmptyStateComponent,
  LineChartComponent,
  PageContainerComponent,
  RelativeTimePipe,
  SkeletonComponent,
  StatCardComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ProviderLabelPipe} from '../../../../shared/pipes/provider-label.pipe';
import {AuthStore} from '../../../auth/store/auth.store';
import {type DossierSignalItem} from '../../models/dossier/dossier.model';
import {CoverageLabelPipe, SignalTypeLabelPipe} from '../../pipes/dossier-label.pipe';
import {MarkdownPipe} from '../../pipes/markdown.pipe';
import {TriggerSentencePipe} from '../../pipes/trigger-sentence.pipe';
import {DossierStore} from '../../store/dossier.store';

const SEVERITY_VALUE: Record<string, number> = {high: 25, medium: 15, low: 5};
const SEVERITY_DEFAULT = 5;
const TREND_MIN_POINTS = 2;
const TREND_SCALE_MAX = 30;
const TREND_LABEL_FORMAT = 'mediumDate';
const TREND_LOCALE = 'en-US';

@Component({
  selector: 'fns-asset-dossier',
  imports: [
    PageContainerComponent,
    AlertComponent,
    ButtonComponent,
    CardComponent,
    CmnCellDirective,
    CmnColumnComponent,
    CoverageLabelPipe,
    DataTableComponent,
    DatePipe,
    DecimalPipe,
    MarkdownPipe,
    MoneyPipe,
    ProviderLabelPipe,
    RelativeTimePipe,
    RouterLink,
    SignalTypeLabelPipe,
    SkeletonComponent,
    EmptyStateComponent,
    LineChartComponent,
    StatCardComponent,
    TagComponent,
    TriggerSentencePipe,
  ],
  templateUrl: './asset-dossier.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DossierStore],
})
export class AssetDossierComponent {
  public readonly canUseAi = inject(AuthStore).canUseAi;
  public readonly store = inject(DossierStore);
  public readonly accountsRoute = AppRoute.AccountsInvestments;
  public readonly pnlPositiveClass = 'text-status-success';
  public readonly pnlNegativeClass = 'text-status-error';

  public readonly trendDomain: ChartDomain = {min: 0, max: TREND_SCALE_MAX};

  public readonly radarTrendPoints = computed((): ChartPoint[] | null => {
    const signals = this.store.dossier()?.radarSignals ?? [];
    if (signals.length < TREND_MIN_POINTS) {
      return null;
    }
    return [...signals]
      .sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime())
      .map(s => ({
        label: formatDate(s.timestamp, TREND_LABEL_FORMAT, TREND_LOCALE),
        time: new Date(s.timestamp).getTime(),
        value: SEVERITY_VALUE[s.severity] ?? SEVERITY_DEFAULT,
      }));
  });

  public readonly latestSignal = computed((): DossierSignalItem | null => {
    const signals = this.store.dossier()?.radarSignals ?? [];
    if (signals.length === 0) {
      return null;
    }
    return [...signals].sort(
      (a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime()
    )[0];
  });

  public generateLedgerRead(symbol: string, force: boolean): void {
    this.store.generateLedgerRead({symbol, force});
  }
}
