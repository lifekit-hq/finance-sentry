import type {Page, TestInfo, TestType} from '@playwright/test';

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- Playwright's own fixture generics
type AnyTest = TestType<any, any>;

export const REPORT_ONLY_ANNOTATION = 'report-only';

/**
 * Returns a `test` whose tests do not fail on what the gates find: a failure inside the body is
 * recorded as a `report-only` annotation on the test, printed, and the test passes. The shared
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
      if (info.expectedStatus === 'skipped') {
        throw error;
      }
      const description = error instanceof Error ? error.message : String(error);
      info.annotations.push({type: REPORT_ONLY_ANNOTATION, description});
      // eslint-disable-next-line no-console -- the CI log is where a report-only run is read
      console.log(`[report-only] ${info.titlePath.slice(1).join(' > ')}\n${description}\n`);
    }
  };
}
