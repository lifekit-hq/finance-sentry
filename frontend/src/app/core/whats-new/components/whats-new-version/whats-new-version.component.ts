import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, input} from '@angular/core';

import {type WhatsNewVersion} from '../../models/whats-new.model';

/** One release in the panel: its date, the plain lines, and a fold with every filtered change. */
@Component({
  selector: 'fns-whats-new-version',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section [attr.data-testid]="'whats-new-' + version().version">
      <header class="flex items-baseline justify-between gap-cmn-3">
        <h2 class="font-headline text-cmn-lg font-semibold text-text-primary">
          {{ version().version }}
        </h2>
        @if (version().date; as date) {
          <time [attr.datetime]="date" class="text-cmn-xs text-text-secondary">{{
            date | date: 'MMM d, y'
          }}</time>
        }
      </header>

      @if (hasNotes()) {
        <ul class="mt-cmn-3 list-disc space-y-cmn-2 pl-cmn-5 text-cmn-sm text-text-primary">
          @for (note of version().notes; track note.text) {
            <li data-testid="whats-new-note">{{ note.text }}</li>
          }
        </ul>
      }

      @if (version().changes.length > 0) {
        <details [open]="!hasNotes()" class="group mt-cmn-3" data-testid="whats-new-changes">
          <summary
            class="cursor-pointer text-cmn-sm font-medium text-accent-default focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-border-focus"
          >
            Every change in this version
          </summary>
          <ul class="mt-cmn-2 list-disc space-y-cmn-1 pl-cmn-5 text-cmn-xs text-text-secondary">
            @for (change of version().changes; track change) {
              <li>{{ change }}</li>
            }
          </ul>
        </details>
      } @else if (!hasNotes()) {
        <p class="mt-cmn-3 text-cmn-sm text-text-secondary">No changes to show for this version.</p>
      }
    </section>
  `,
})
export class WhatsNewVersionComponent {
  public readonly version = input.required<WhatsNewVersion>();

  public readonly hasNotes = computed(() => this.version().notes.length > 0);
}
