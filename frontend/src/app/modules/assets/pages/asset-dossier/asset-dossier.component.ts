import {DatePipe, DecimalPipe, DOCUMENT, formatDate, NgTemplateOutlet} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
  signal,
} from '@angular/core';
import {FormsModule} from '@angular/forms';
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
  FormFieldComponent,
  IconComponent,
  InputComponent,
  InstitutionAvatarComponent,
  LineChartComponent,
  PageComponent,
  PriceBlockComponent,
  RelativeTimePipe,
  SegmentedComponent,
  SkeletonComponent,
  TagComponent,
  type TagVariant,
  TileGridComponent,
  TilePanelDirective,
  VerdictHeaderComponent,
} from '@lifekit-hq/ui';

import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {APP_LOCALE} from '../../../../shared/constants/date/date.constants';
import {
  VERDICT_DISPLAY_LABEL,
  VERDICT_DISPLAY_OPTIONS,
} from '../../../../shared/constants/preferences/app-preferences.constants';
import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AssetLogoPipe} from '../../../../shared/pipes/asset-logo.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ProviderLabelPipe} from '../../../../shared/pipes/provider-label.pipe';
import {TriggerSentencePipe} from '../../../../shared/pipes/trigger-sentence.pipe';
import {UnitPricePipe} from '../../../../shared/pipes/unit-price.pipe';
import {AuthStore} from '../../../auth/store/auth.store';
import {RecommendationCardComponent} from '../../../decisions/components/recommendation-card/recommendation-card.component';
import {ThesisRevisionCardComponent} from '../../../decisions/components/thesis-revision-card/thesis-revision-card.component';
import {DECISION_RECORDED_TITLE} from '../../../decisions/constants/recommendation/recommendation.constants';
import {
  THESIS_REVISION_KIND_LABEL,
  THESIS_REVISION_RECORDED_TITLE,
} from '../../../decisions/constants/thesis-revision/thesis-revision.constants';
import {RecommendationOutcomePipe} from '../../../decisions/pipes/recommendation.pipe';
import {DisposalTaxComponent} from '../../components/disposal-tax/disposal-tax.component';
import {PriceChartComponent} from '../../components/price-chart/price-chart.component';
import {VerdictBlockComponent} from '../../components/verdict-block/verdict-block.component';
import {CHART_SKELETON_HEIGHT} from '../../constants/dossier/dossier-chart.constants';
import {OWNERSHIP_NOTE} from '../../constants/dossier/dossier-facts.constants';
import {
  DOSSIER_FACTS_ID,
  DOSSIER_SECTION_ID,
  NO_LINKED_MOVE_ITEMS_MESSAGE,
} from '../../constants/dossier/dossier-sections.constants';
import {MOVE_ICON} from '../../constants/dossier/dossier-tiles.constants';
import {
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
    ThesisRevisionCardComponent,
    PageComponent,
    AlertComponent,
    AnalystActionLabelPipe,
    PriceBlockComponent,
    AssetLogoPipe,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    CoverageLabelPipe,
    DatePipe,
    DecimalPipe,
    DisposalTaxComponent,
    InstitutionAvatarComponent,
    MarkdownPipe,
    MoneyPipe,
    UnitPricePipe,
    NewsSourceLabelPipe,
    PeerSetLabelPipe,
    ProviderLabelPipe,
    RelativeTimePipe,
    RouterLink,
    SegmentedComponent,
    PriceChartComponent,
    VerdictBlockComponent,
    SignalTypeLabelPipe,
    SkeletonComponent,
    EmptyStateComponent,
    FormFieldComponent,
    FormsModule,
    IconComponent,
    InputComponent,
    InputHintsDirective,
    LineChartComponent,
    NgTemplateOutlet,
    TagComponent,
    TileGridComponent,
    TilePanelDirective,
    TriggerSentencePipe,
    VerdictHeaderComponent,
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
  public readonly accountsRoute = AppRoute.Accounts;
  public readonly decisionsRoute = AppRoute.LedgerDecisions;
  public readonly decisionRecordedTitle = DECISION_RECORDED_TITLE;
  public readonly revisionRecordedTitle = THESIS_REVISION_RECORDED_TITLE;
  public readonly kindLabel = THESIS_REVISION_KIND_LABEL;
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
  public readonly factsId = DOSSIER_FACTS_ID;
  public readonly moveIcon = MOVE_ICON;
  // The one open tile of the facts grid; a chip, a fragment or a move's signal link moves it.
  public readonly openTile = signal<string | null>(null);
  public readonly noLinkedItemsMessage = NO_LINKED_MOVE_ITEMS_MESSAGE;
  public readonly chartSkeletonHeight = CHART_SKELETON_HEIGHT;
  public readonly ownershipNote = OWNERSHIP_NOTE;
  public readonly verdictDisplayOptions = VERDICT_DISPLAY_OPTIONS;
  public readonly verdictDisplayLabel = VERDICT_DISPLAY_LABEL;
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
    // A section that is a tile has no element of its own: open it and land on the grid.
    const isTile = this.store.factTiles().some(tile => tile.id === id);
    if (isTile) {
      this.openTile.set(id);
    }
    this.document
      .getElementById(isTile ? this.factsId : id)
      ?.scrollIntoView({behavior: 'smooth', block: 'start'});
  }

  // The tick-strip template's context is untyped; this keeps the lookup typed.
  public tickClass(state: RuleState): string {
    return this.ruleTickClass[state];
  }

  public generateLedgerRead(symbol: string, force: boolean): void {
    this.store.generateLedgerRead({symbol, force});
  }
}
