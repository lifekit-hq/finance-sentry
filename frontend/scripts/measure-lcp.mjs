// Cold /login LCP at a 390px viewport on Slow 4G with a 4x CPU slowdown - the Core Web Vitals
// field-test shape, in headless Chromium. Reports the median of RUNS cold loads against the 2.5 s
// "good" mark and fails only when the median exceeds the regression budget in perf-budget.json.
// Usage: node scripts/measure-lcp.mjs <base-url>
import {appendFileSync, readFileSync} from 'node:fs';
import {chromium} from '@playwright/test';

const GOOD_LCP_MS = 2500;
const RUNS = 5;
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
const {lcpRegressionBudgetMs} = JSON.parse(
  readFileSync(new URL('../perf-budget.json', import.meta.url), 'utf8')
);

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
    window.__lcp = 0;
    new PerformanceObserver(list => {
      for (const entry of list.getEntries()) window.__lcp = entry.startTime;
    }).observe({type: 'largest-contentful-paint', buffered: true});
  });
  await page.goto(`${baseUrl}/login`, {waitUntil: 'networkidle'});
  samples.push(await page.evaluate(() => window.__lcp));
  await context.close();
}
await browser.close();

const sorted = [...samples].sort((a, b) => a - b);
const median = sorted[Math.floor(sorted.length / 2)];
const verdict = median <= GOOD_LCP_MS ? 'within' : 'above';
const lines = [
  `Cold /login LCP, 390px, Slow 4G, ${CPU_SLOWDOWN}x CPU: median ${Math.round(median)} ms`,
  `runs: ${samples.map(s => Math.round(s)).join(', ')} ms`,
  `Core Web Vitals "good" mark: ${GOOD_LCP_MS} ms - ${verdict} (${(median / GOOD_LCP_MS).toFixed(2)}x)`,
  `regression budget: ${lcpRegressionBudgetMs} ms`,
];
console.log(lines.join('\n'));
if (process.env['GITHUB_STEP_SUMMARY']) {
  appendFileSync(
    process.env['GITHUB_STEP_SUMMARY'],
    `### Cold /login LCP\n\n${lines.map(l => `- ${l}`).join('\n')}\n`
  );
}
if (median > lcpRegressionBudgetMs) {
  console.error(
    `LCP regression: ${Math.round(median)} ms exceeds the ${lcpRegressionBudgetMs} ms budget`
  );
  process.exit(1);
}
