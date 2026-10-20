import {HttpErrorResponse} from '@angular/common/http';
import {type ErrorHandler, inject, Injectable} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';
import {type Nullable, ToastService} from '@lifekit-hq/ui';

const UNAUTHORIZED_STATUS = 401;
const GENERIC_ERROR = 'An unexpected error occurred.';

interface ApiErrorBody {
  errorCode?: string;
  message?: string;
}

@Injectable()
export class HttpErrorHandler implements ErrorHandler {
  private readonly toastService = inject(ToastService);
  private readonly errorMessages = inject(ErrorMessageService);

  public handleError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === UNAUTHORIZED_STATUS) {
        return;
      }
      const body = error.error as Nullable<ApiErrorBody>;
      const message = this.errorMessages.resolve(body?.errorCode) ?? body?.message ?? GENERIC_ERROR;
      this.toastService.error(message);
    }
    console.error(error);
  }
}
