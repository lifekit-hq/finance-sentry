import type {Page, TestInfo, TestType} from '@playwright/test';

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- Playwright's own fixture generics
type AnyTest = TestType<any, any>;

export const REPORT_ONLY_ANNOTATION = 'report-only';

/** A failure to get a route onto the screen (prepare, ready): it fails the test in every mode. */
export class SetupError extends Error {
  public constructor(step: string, cause: unknown) {
    super(`${step} failed: ${cause instanceof Error ? cause.message : String(cause)}`, {cause});
    this.name = 'SetupError';
  }
}

/** What a gate found, thrown by a gate body that has no matcher of its own. */
export class FindingError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = 'FindingError';
  }
}

/** Runs one setup step; whatever it throws is a `SetupError`, which `reportingBody` never swallows. */
export async function asSetup(step: string, run: () => Promise<void>): Promise<void> {
  try {
    await run();
  } catch (error) {
    throw new SetupError(step, error);
  }
}

function isFinding(error: unknown): boolean {
  return (
    error instanceof FindingError ||
    (typeof error === 'object' && error !== null && 'matcherResult' in error)
  );
}

/**
 * Returns a `test` whose tests do not fail on what the gates find: a finding inside the body (a
 * failed matcher, a `FindingError`) is recorded as a `report-only` annotation on the test, printed,
 * and the test passes. Anything else (a failed `prepare` or `ready`, a navigation or settle timeout,
 * a broken mock) fails the test: a route the gates could not open has not been checked. The shared
 * fixtures (`defineScreenBaselines`, `defineRouteScans`) take a `test` and register their tests on
 * it, so handing them this one keeps their own wiring (sizes, schemes, viewports, settling) intact
 * while a route is being adopted. Everything but the call itself (`describe`, `use`, `skip`, ...)
 * is the real `test`'s.
 *
 * A skip is not a finding and is re-thrown.
 */
export function reportOnly(test: AnyTest): AnyTest {
  return new Proxy(test, {
    apply(target, thisArg, args: unknown[]) {
      const [title, body] = args as [string, ScreenTestBody];
      return Reflect.apply(target, thisArg, [title, reportingBody(body)]);
    },
  });
}

/** The body of a gate test: the fixtures take nothing but the page. */
export type ScreenTestBody = (fixtures: {page: Page}, info: TestInfo) => Promise<void>;

export function reportingBody(body: ScreenTestBody): ScreenTestBody {
  // Playwright reads the fixtures a test uses off the first parameter, so it has to be destructured.
  return async ({page}, info) => {
    try {
      await body({page}, info);
    } catch (error) {
      if (info.expectedStatus === 'skipped' || !isFinding(error)) {
        throw error;
      }
      const description = error instanceof Error ? error.message : String(error);
      info.annotations.push({type: REPORT_ONLY_ANNOTATION, description});
      // eslint-disable-next-line no-console -- the CI log is where a report-only run is read
      console.log(`[report-only] ${info.titlePath.slice(1).join(' > ')}\n${description}\n`);
    }
  };
}
