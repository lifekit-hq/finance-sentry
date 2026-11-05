import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, input} from '@angular/core';
import {TagComponent} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {
  DISPOSAL_TAX_CARRIED_FORWARD_NOTE,
  DISPOSAL_TAX_EXEMPTION_NOTE,
  DISPOSAL_TAX_EXIT_NOTE,
  DISPOSAL_TAX_REGIME_LABEL,
  DISPOSAL_TAX_WITHHELD_TITLE,
} from '../../constants/dossier/disposal-tax.constants';
import {type DisposalTaxContextDto} from '../../models/dossier/dossier.model';
import {DisposalTaxUtils} from '../../utils/disposal-tax.utils';

/**
 * "If you sold this today" for a held position (#454): the Irish regime and a rough euro tax, collapsed in a
 * `<details>`. When the data cannot support a figure it says so and lists why instead of guessing. Context only.
 */
@Component({
  selector: 'fns-disposal-tax',
  imports: [DatePipe, MoneyPipe, TagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './disposal-tax.component.html',
})
export class DisposalTaxComponent {
  public readonly context = input.required<DisposalTaxContextDto>();

  public readonly withheldTitle = DISPOSAL_TAX_WITHHELD_TITLE;
  public readonly exemptionNote = DISPOSAL_TAX_EXEMPTION_NOTE;
  public readonly exitNote = DISPOSAL_TAX_EXIT_NOTE;
  public readonly carriedForwardNote = DISPOSAL_TAX_CARRIED_FORWARD_NOTE;

  public readonly isShown = computed(() => this.context().status === 'shown');
  public readonly regimeLabel = computed(() => {
    const regime = this.context().regime;
    return regime === null ? null : DISPOSAL_TAX_REGIME_LABEL[regime];
  });
  public readonly ratePercent = computed(() => DisposalTaxUtils.ratePercent(this.context().rate));
  public readonly isExitTax = computed(() => this.context().regime === 'exitTax');
  public readonly reasons = computed(() =>
    this.context().withheldReasons.map(code => DisposalTaxUtils.reasonText(code))
  );
}
