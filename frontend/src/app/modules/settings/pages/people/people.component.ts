import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  ViewContainerRef,
} from '@angular/core';
import {FormBuilder, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  FormFieldComponent,
  InputComponent,
  ListItemRowComponent,
  PageContainerComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {PERSON_STATUS_TAG_VARIANT} from '../../constants/person/person.constants';
import {type PersonRow} from '../../models/person/person.model';
import {PeopleStore} from '../../store/people/people.store';

const SKELETON_ROWS = 3;

@Component({
  selector: 'fns-people',
  imports: [
    PageContainerComponent,
    ReactiveFormsModule,
    AlertComponent,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    FormFieldComponent,
    InputComponent,
    ListItemRowComponent,
    SkeletonComponent,
    TagComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [PeopleStore],
  templateUrl: './people.component.html',
})
export class PeopleComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(PeopleStore);
  // Offline only reaches `resolve` while nothing is on screen: with rows, a failed reload keeps
  // its error banner instead of reading as a successful (stale) load.
  public readonly view = computed(() => {
    const hasData = this.store.rows().length > 0;
    return AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData,
      errorMessage: this.store.loadErrorMessage(),
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
  public readonly statusVariant = PERSON_STATUS_TAG_VARIANT;
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS}, (_, i) => i);
  public readonly inviteForm = inject(FormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
  });

  public createInvite(): void {
    if (this.inviteForm.invalid) {
      return;
    }
    this.store.createInvite({email: this.inviteForm.value.email ?? ''});
    this.inviteForm.reset();
  }

  public requestRevoke(person: PersonRow): void {
    const ref = this.dialog.open<boolean>(ConfirmDialogComponent, {
      data: {
        title: `Revoke access for ${person.email}?`,
        message: 'They are signed out on their next request and can no longer sign in.',
        confirmLabel: 'Revoke access',
        cancelLabel: 'Cancel',
        confirmVariant: 'destructive',
      },
      size: 'sm',
      viewContainerRef: this.viewContainerRef,
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.revoke(person.id);
        }
      });
  }
}
