import {DatePipe, DecimalPipe, DOCUMENT, formatDate} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {RouterLink} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
  type AsyncStateStatus,
  ButtonComponent,
  CardComponent,
  type ChartDomain,
  type ChartPoint,
  ChipComponent,
  EmptyStateComponent,
  InstitutionAvatarComponent,
  LineChartComponent,
  PageContainerComponent,
  RelativeTimePipe,
  SkeletonComponent,
  StatCardComponent,
  TagComponent,
  type TagVariant,
} from '@lifekit-hq/ui';

import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {APP_LOCALE} from '../../../../shared/constants/date/date.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ProviderLabelPipe} from '../../../../shared/pipes/provider-label.pipe';
import {AuthStore} from '../../../auth/store/auth.store';
import {
  type DossierDeltaDirection,
  type DossierSignalItem,
} from '../../models/dossier/dossier.model';
import {
  AnalystActionLabelPipe,
  CoverageLabelPipe,
  DividendYieldPipe,
  NewsSourceLabelPipe,
  PeerSetLabelPipe,
  SignalTypeLabelPipe,
} from '../../pipes/dossier-label.pipe';
import {MarkdownPipe} from '../../pipes/markdown.pipe';
import {TriggerSentencePipe} from '../../pipes/trigger-sentence.pipe';
import {DossierStore} from '../../store/dossier.store';

const SIGNAL_SEVERITY_VARIANT: Record<string, TagVariant> = {
  alerted: 'error',
  notable: 'warning',
  info: 'info',
};
const SEVERITY_VALUE: Record<string, number> = {alerted: 25, notable: 15, info: 5};
const SEVERITY_DEFAULT = 5;
const TREND_MIN_POINTS = 2;
const TREND_SCALE_MAX = 30;
const TREND_LABEL_FORMAT = 'mediumDate';

@Component({
  selector: 'fns-asset-dossier',
  imports: [
    PageContainerComponent,
    AlertComponent,
    AnalystActionLabelPipe,
    AssetLogoPipe,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    CoverageLabelPipe,
    DatePipe,
    DecimalPipe,
    DividendYieldPipe,
    InstitutionAvatarComponent,
    MarkdownPipe,
    MoneyPipe,
    NewsSourceLabelPipe,
    PeerSetLabelPipe,
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
  private readonly document = inject(DOCUMENT);

  public readonly canUseAi = inject(AuthStore).canUseAi;
  public readonly store = inject(DossierStore);
  public readonly accountsRoute = AppRoute.AccountsInvestments;
  public readonly dossierStatus = computed<AsyncStateStatus>(() => {
    if (this.store.dossierErrorMessage()) {
      return 'error';
    }
    return this.store.isDossierLoading() ? 'loading' : 'success';
  });
  public readonly severityVariant = SIGNAL_SEVERITY_VARIANT;
  public readonly defaultSeverityVariant: TagVariant = 'neutral';
  public readonly pnlPositiveClass = GAIN_TEXT_CLASS;
  public readonly pnlNegativeClass = LOSS_TEXT_CLASS;
  // Only the delta carries colour; the header itself stays neutral.
  public readonly deltaClass: Record<DossierDeltaDirection, string> = {
    up: GAIN_TEXT_CLASS,
    down: LOSS_TEXT_CLASS,
    flat: 'text-text-secondary',
  };

  public readonly trendDomain: ChartDomain = {min: 0, max: TREND_SCALE_MAX};

  public readonly radarTrendPoints = computed((): ChartPoint[] | null => {
    const signals = this.store.dossier()?.radarSignals ?? [];
    if (signals.length < TREND_MIN_POINTS) {
      return null;
    }
    return [...signals]
      .sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime())
      .map(s => ({
        label: formatDate(s.timestamp, TREND_LABEL_FORMAT, APP_LOCALE),
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

  public scrollToSection(id: string): void {
    this.document.getElementById(id)?.scrollIntoView({behavior: 'smooth', block: 'start'});
  }

  public generateLedgerRead(symbol: string, force: boolean): void {
    this.store.generateLedgerRead({symbol, force});
  }
}
