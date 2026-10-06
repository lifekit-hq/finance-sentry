// Cold /login LCP at a 390px viewport on Slow 4G with a 4x CPU slowdown - the Core Web Vitals
// field-test shape, in headless Chromium. Reports the median of RUNS cold loads against the 2.5 s
// "good" mark and fails only when the median exceeds LCP_REGRESSION_BUDGET_MS.
// Usage: node scripts/measure-lcp.mjs <base-url>
import {appendFileSync} from 'node:fs';
import {chromium} from '@playwright/test';

const GOOD_LCP_MS = 2500;
const LCP_REGRESSION_BUDGET_MS = 4500;
const RUNS = 5;
const MEASURED_CONTENT = 'form';
const LCP_SETTLE_MS = 1000;
const SETTLE_POLL_MS = 100;
const VIEWPORT = {width: 390, height: 844};
const DEVICE_SCALE_FACTOR = 3;
const CPU_SLOWDOWN = 4;
const BYTES_PER_KBIT = 125;
// DevTools "Slow 4G" preset.
const SLOW_4G = {
  offline: false,
  latency: 150,
  downloadThroughput: 1600 * BYTES_PER_KBIT,
  uploadThroughput: 750 * BYTES_PER_KBIT,
};

const baseUrl = process.argv[2];
if (!baseUrl) throw new Error('usage: node scripts/measure-lcp.mjs <base-url>');

const browser = await chromium.launch({args: ['--no-sandbox', '--disable-dev-shm-usage']});
const samples = [];
for (let run = 0; run < RUNS; run += 1) {
  // A fresh context per run: empty HTTP cache, no service worker - a first visit.
  const context = await browser.newContext({
    viewport: VIEWPORT,
    deviceScaleFactor: DEVICE_SCALE_FACTOR,
    isMobile: true,
    hasTouch: true,
    serviceWorkers: 'block',
  });
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', SLOW_4G);
  await cdp.send('Emulation.setCPUThrottlingRate', {rate: CPU_SLOWDOWN});
  await page.addInitScript(() => {
    window.__lcp = null;
    window.__lcpObservedAt = 0;
    new PerformanceObserver(list => {
      for (const entry of list.getEntries()) {
        window.__lcp = entry.startTime;
        window.__lcpObservedAt = performance.now();
      }
    }).observe({type: 'largest-contentful-paint', buffered: true});
  });
  // Everything the browser saw, printed only when the page never renders the measured content:
  // nginx logs its static assets with access_log off, so this is the only record of them in CI.
  const observed = [];
  page.on('response', res =>
    observed.push(`${res.status()} ${res.headers()['content-encoding'] ?? '-'} ${res.url()}`)
  );
  page.on('requestfailed', req =>
    observed.push(`failed ${req.failure()?.errorText ?? '?'} ${req.url()}`)
  );
  page.on('console', msg => {
    if (msg.type() === 'error') observed.push(`console: ${msg.text()}`);
  });
  page.on('pageerror', err => observed.push(`pageerror: ${err.message}`));
  await page.goto(`${baseUrl}/login`, {waitUntil: 'networkidle'});
  try {
    await page.locator(MEASURED_CONTENT).waitFor();
  } catch (err) {
    console.error(
      `run ${run + 1}: "${MEASURED_CONTENT}" never rendered; the browser observed:\n${observed.join('\n')}`
    );
    throw err;
  }
  await page.waitForFunction(
    settleMs => performance.now() - window.__lcpObservedAt >= settleMs,
    LCP_SETTLE_MS,
    {polling: SETTLE_POLL_MS}
  );
  samples.push(await page.evaluate(() => window.__lcp));
  await context.close();
}
await browser.close();

const unpainted = samples.filter(sample => !(sample > 0)).length;
const sorted = samples.filter(sample => sample > 0).sort((a, b) => a - b);
const median = sorted[Math.floor(sorted.length / 2)] ?? NaN;
const verdict = median <= GOOD_LCP_MS ? 'within' : 'above';
const formatMs = ms => (ms > 0 ? `${Math.round(ms)} ms` : 'no paint');
const lines = [
  `Cold /login LCP, 390px, Slow 4G, ${CPU_SLOWDOWN}x CPU: median ${formatMs(median)}`,
  `runs: ${samples.map(formatMs).join(', ')}`,
  `Core Web Vitals "good" mark: ${GOOD_LCP_MS} ms - ${verdict} (${(median / GOOD_LCP_MS).toFixed(2)}x)`,
  `regression budget: ${LCP_REGRESSION_BUDGET_MS} ms`,
];
if (unpainted > 0) {
  lines.push(`FAILED: ${unpainted} of ${RUNS} runs produced no largest-contentful-paint entry`);
}
console.log(lines.join('\n'));
if (process.env['GITHUB_STEP_SUMMARY']) {
  appendFileSync(
    process.env['GITHUB_STEP_SUMMARY'],
    `### Cold /login LCP\n\n${lines.map(l => `- ${l}`).join('\n')}\n`
  );
}
if (unpainted > 0) {
  console.error(
    `LCP not observed: ${unpainted} of ${RUNS} runs produced no largest-contentful-paint entry`
  );
  process.exit(1);
}
if (median > LCP_REGRESSION_BUDGET_MS) {
  console.error(
    `LCP regression: ${Math.round(median)} ms exceeds the ${LCP_REGRESSION_BUDGET_MS} ms budget`
  );
  process.exit(1);
}
