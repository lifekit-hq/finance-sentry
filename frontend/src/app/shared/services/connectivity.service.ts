import {DOCUMENT} from '@angular/common';
import {inject, Injectable} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {fromEvent, map, merge} from 'rxjs';

/** The browser's online/offline events as a signal; screens read it to show "last synced". */
@Injectable({providedIn: 'root'})
export class ConnectivityService {
  private readonly win = inject(DOCUMENT).defaultView;

  public readonly offline = toSignal(
    this.win === null
      ? merge()
      : merge(
          fromEvent(this.win, 'online').pipe(map(() => false)),
          fromEvent(this.win, 'offline').pipe(map(() => true))
        ),
    {initialValue: this.win?.navigator.onLine === false}
  );
}
