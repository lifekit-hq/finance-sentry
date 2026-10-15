import {DatePipe, DecimalPipe, DOCUMENT, formatDate, NgTemplateOutlet} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {RouterLink} from '@angular/router';
import {type ChipRowChangeDetail} from '@lifekit-hq/elements';
import {
  AlertComponent,
  AreaChartComponent,
  AsyncStateComponent,
  type AsyncStateStatus,
  ButtonComponent,
  CardComponent,
  type ChartDomain,
  type ChartPoint,
  ChipComponent,
  EmptyStateComponent,
  FormFieldComponent,
  InputComponent,
  InstitutionAvatarComponent,
  LineChartComponent,
  PageContainerComponent,
  RelativeTimePipe,
  SkeletonComponent,
  TagComponent,
  type TagVariant,
} from '@lifekit-hq/ui';

import {ScorecardVerdictComponent} from '../../../../shared/components/scorecard-verdict/scorecard-verdict.component';
import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {APP_LOCALE} from '../../../../shared/constants/date/date.constants';
import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ProviderLabelPipe} from '../../../../shared/pipes/provider-label.pipe';
import {AuthStore} from '../../../auth/store/auth.store';
import {RecommendationCardComponent} from '../../../decisions/components/recommendation-card/recommendation-card.component';
import {DECISION_RECORDED_TITLE} from '../../../decisions/constants/recommendation/recommendation.constants';
import {RecommendationOutcomePipe} from '../../../decisions/pipes/recommendation.pipe';
import {CHART_RANGES, CHART_SKELETON_HEIGHT} from '../../constants/dossier/dossier-chart.constants';
import {OWNERSHIP_NOTE} from '../../constants/dossier/dossier-facts.constants';
import {DOSSIER_SECTION_ID} from '../../constants/dossier/dossier-sections.constants';
import {
  type ChartRangeId,
  type DossierDeltaDirection,
  type DossierSignalItem,
} from '../../models/dossier/dossier.model';
import {type RuleState} from '../../models/dossier/dossier-view.model';
import {
  AnalystActionLabelPipe,
  CoverageLabelPipe,
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
    RecommendationCardComponent,
    RecommendationOutcomePipe,
    PageContainerComponent,
    AlertComponent,
    AnalystActionLabelPipe,
    AreaChartComponent,
    AssetLogoPipe,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    CoverageLabelPipe,
    DatePipe,
    DecimalPipe,
    InstitutionAvatarComponent,
    MarkdownPipe,
    MoneyPipe,
    NewsSourceLabelPipe,
    PeerSetLabelPipe,
    ProviderLabelPipe,
    RelativeTimePipe,
    RouterLink,
    ScorecardVerdictComponent,
    SignalTypeLabelPipe,
    SkeletonComponent,
    EmptyStateComponent,
    FormFieldComponent,
    FormsModule,
    InputComponent,
    InputHintsDirective,
    LineChartComponent,
    NgTemplateOutlet,
    TagComponent,
    TriggerSentencePipe,
  ],
  templateUrl: './asset-dossier.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // lk-chip-row (the chart's timeframe range) is a custom element from @lifekit-hq/elements.
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [DossierStore],
})
export class AssetDossierComponent {
  private readonly document = inject(DOCUMENT);

  public readonly canUseAi = inject(AuthStore).canUseAi;
  public readonly store = inject(DossierStore);
  public readonly accountsRoute = AppRoute.AccountsInvestments;
  public readonly decisionsRoute = AppRoute.LedgerDecisions;
  public readonly decisionRecordedTitle = DECISION_RECORDED_TITLE;
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

  public readonly sectionId = DOSSIER_SECTION_ID;
  public readonly chartRanges = CHART_RANGES;
  public readonly chartSkeletonHeight = CHART_SKELETON_HEIGHT;
  public readonly ownershipNote = OWNERSHIP_NOTE;
  // A tick per rule: passed, failed, or not evaluable on the data on hand.
  public readonly ruleTickClass: Record<RuleState, string> = {
    pass: 'bg-gain',
    fail: 'bg-loss',
    unknown: 'bg-border-strong',
  };
  public readonly ruleStateLabel: Record<RuleState, string> = {
    pass: 'Pass',
    fail: 'Fail',
    unknown: 'No data',
  };
  public readonly toneClass: Record<'gain' | 'loss', string> = {
    gain: GAIN_TEXT_CLASS,
    loss: LOSS_TEXT_CLASS,
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

  // The tick-strip template's context is untyped; this keeps the lookup typed.
  public tickClass(state: RuleState): string {
    return this.ruleTickClass[state];
  }

  public onChartRangeChange(event: Event): void {
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.selectChartRange(value as ChartRangeId);
  }

  public generateLedgerRead(symbol: string, force: boolean): void {
    this.store.generateLedgerRead({symbol, force});
  }
}
