import {Clipboard} from '@angular/cdk/clipboard';
import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject, ViewContainerRef} from '@angular/core';
import {FormBuilder, FormsModule, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  EmptyStateComponent,
  FormFieldComponent,
  InputComponent,
  ListItemRowComponent,
  PageContainerComponent,
  SkeletonComponent,
  TagComponent,
  ToastService,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {PERSON_STATUS_TAG_VARIANT} from '../../constants/person/person.constants';
import {type PersonRow} from '../../models/person/person.model';
import {PeopleStore} from '../../store/people/people.store';

const SKELETON_ROWS = 3;

@Component({
  selector: 'fns-people',
  imports: [
    PageContainerComponent,
    DatePipe,
    FormsModule,
    ReactiveFormsModule,
    AlertComponent,
    ButtonComponent,
    CardComponent,
    EmptyStateComponent,
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
  private readonly clipboard = inject(Clipboard);
  private readonly dialog = inject(CmnDialogService);
  private readonly toast = inject(ToastService);
  private readonly viewContainerRef = inject(ViewContainerRef);

  public readonly store = inject(PeopleStore);
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

  public copyInviteLink(link: string): void {
    if (this.clipboard.copy(link)) {
      this.toast.show('Invite link copied', 'success');
    } else {
      this.toast.show('Could not copy the link. Select it and copy it manually.', 'error');
    }
  }

  public requestRevoke(person: PersonRow): void {
    const ref = this.dialog.open<boolean>(ConfirmDialogComponent, {
      data: {
        title: `Revoke access for ${person.email}?`,
        message:
          'They are signed out on their next request and can no longer sign in. Any unused invite link stops working.',
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
