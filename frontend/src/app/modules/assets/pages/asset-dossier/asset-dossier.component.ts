import {DatePipe, DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  CmnCellDirective,
  CmnColumnComponent,
  DataTableComponent,
  EmptyStateComponent,
  SkeletonComponent,
  StatCardComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ProviderLabelPipe} from '../../../../shared/pipes/provider-label.pipe';
import {RelativeTimePipe} from '../../../../shared/pipes/relative-time.pipe';
import {AuthStore} from '../../../auth/store/auth.store';
import {type DossierSignalItem} from '../../models/dossier/dossier.model';
import {CoverageLabelPipe, SignalTypeLabelPipe} from '../../pipes/dossier-label.pipe';
import {MarkdownPipe} from '../../pipes/markdown.pipe';
import {TriggerSentencePipe} from '../../pipes/trigger-sentence.pipe';
import {DossierStore} from '../../store/dossier.store';

const SEVERITY_VALUE: Record<string, number> = {high: 25, medium: 15, low: 5};
const SEVERITY_DEFAULT = 5;
const SPARKLINE_WIDTH = 96;
const SPARKLINE_MARGIN = 2;
const SPARKLINE_HEIGHT = 30;
const SPARKLINE_RIGHT_X = SPARKLINE_WIDTH + SPARKLINE_MARGIN;
const SPARKLINE_MIN_POINTS = 2;

@Component({
  selector: 'fns-asset-dossier',
  imports: [
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
    StatCardComponent,
    TagComponent,
    TriggerSentencePipe,
  ],
  templateUrl: './asset-dossier.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DossierStore],
})
export class AssetDossierComponent {
  private readonly router = inject(Router);
  public readonly canUseAi = inject(AuthStore).canUseAi;
  public readonly store = inject(DossierStore);
  public readonly accountsRoute = AppRoute.AccountsInvestments;
  public readonly pnlPositiveClass = 'text-status-success';
  public readonly pnlNegativeClass = 'text-status-error';

  public readonly radarSparklinePoints = computed(() => {
    const signals = this.store.dossier()?.radarSignals ?? [];
    if (signals.length < SPARKLINE_MIN_POINTS) {
      return null;
    }
    const sorted = [...signals].sort(
      (a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()
    );
    const times = sorted.map(s => new Date(s.timestamp).getTime());
    const minTime = Math.min(...times);
    const maxTime = Math.max(...times);
    const timeRange = maxTime - minTime || 1;
    return sorted
      .map(s => {
        const x =
          ((new Date(s.timestamp).getTime() - minTime) / timeRange) * SPARKLINE_WIDTH +
          SPARKLINE_MARGIN;
        const y = SPARKLINE_HEIGHT - (SEVERITY_VALUE[s.severity] ?? SEVERITY_DEFAULT);
        return `${x.toFixed(1)},${y}`;
      })
      .join(' ');
  });

  public readonly sparklineFillClose = ` ${SPARKLINE_RIGHT_X},${SPARKLINE_HEIGHT} ${SPARKLINE_MARGIN},${SPARKLINE_HEIGHT}`;

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

  public goBack(): void {
    void this.router.navigate([AppRoute.AccountsInvestments]);
  }
}
