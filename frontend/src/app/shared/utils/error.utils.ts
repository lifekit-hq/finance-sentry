import {type ApiError} from '../models/api/api.model';

export class ErrorUtils {
  public static extractCode(err: unknown): Nullable<string> {
    return (err as {error?: Partial<ApiError>} | null)?.error?.errorCode ?? null;
  }

  public static extractMessage(err: unknown): Nullable<string> {
    return (err as {error?: Partial<ApiError>} | null)?.error?.error ?? null;
  }
}
