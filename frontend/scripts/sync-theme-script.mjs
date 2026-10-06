// Inlines the pre-paint theme script shipped by @lifekit-hq/tokens into src/index.html and pins
// its sha256 in both CSPs (docker/nginx.security-headers.conf and the gateway's
// Gateway:SecurityHeaders in backend/src/FinanceSentry.Gateway/appsettings.json). Angular's
// index.html has no include mechanism, so the bytes are written between the markers; the tokens
// package stays the source.
//   node scripts/sync-theme-script.mjs          rewrite index.html + both CSPs
//   node scripts/sync-theme-script.mjs --check  exit 1 when any is out of sync (lint)
import {readFileSync, writeFileSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const dist = join(root, 'node_modules/@lifekit-hq/tokens/brand/dist');
const INDEX = join(root, 'src/index.html');
const CSP_FILES = [
  join(root, '../docker/nginx.security-headers.conf'),
  join(root, '../backend/src/FinanceSentry.Gateway/appsettings.json'),
];
const BLOCK =
  /(<!-- theme-init:start[^>]*-->\n(?:\s*<!-- prettier-ignore -->\n)?)[\s\S]*?(\n\s*<!-- theme-init:end -->)/;
const HASH = /'sha256-[A-Za-z0-9+/=]+'(?= https:\/\/accounts\.google\.com\/gsi\/client;)/;

export function expected() {
  const script = readFileSync(join(dist, 'theme-init.js'), 'utf8');
  const hash = readFileSync(join(dist, 'theme-init.csp-hash.txt'), 'utf8').trim();
  return {script, hash};
}

export function inlineBlock(html, script) {
  return html.replace(BLOCK, (_m, open, close) => `${open}    <script>${script}</script>${close}`);
}

const {script, hash} = expected();
const html = readFileSync(INDEX, 'utf8');
const nextHtml = inlineBlock(html, script);
const pending = CSP_FILES.map(file => {
  const csp = readFileSync(file, 'utf8');
  return {file, csp, nextCsp: csp.replace(HASH, hash)};
});

if (process.argv.includes('--check')) {
  if (nextHtml !== html || pending.some(({csp, nextCsp}) => nextCsp !== csp)) {
    console.error(
      'theme-init script or CSP hash out of sync: run `node scripts/sync-theme-script.mjs`'
    );
    process.exit(1);
  }
} else {
  writeFileSync(INDEX, nextHtml);
  pending.forEach(({file, nextCsp}) => writeFileSync(file, nextCsp));
}
