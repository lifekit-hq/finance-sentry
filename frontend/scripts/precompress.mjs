// Precompresses the production bundle next to each text asset (.gz and .br) so nginx serves the
// smallest variant with gzip_static / brotli_static instead of compressing per request.
// Usage: node scripts/precompress.mjs [dir]   (default: dist/finance-sentry/browser)
import {readdirSync, readFileSync, statSync, writeFileSync} from 'node:fs';
import {join} from 'node:path';
import {brotliCompressSync, constants, gzipSync} from 'node:zlib';

const COMPRESSIBLE = /\.(?:js|mjs|css|html|svg|json|webmanifest|ico|txt|map)$/i;
// Below this a compressed copy saves less than the extra header/round-trip cost.
const MIN_BYTES = 256;
const GZIP_LEVEL = 9;

const root = process.argv[2] ?? join('dist', 'finance-sentry', 'browser');

function* walk(dir) {
  for (const entry of readdirSync(dir, {withFileTypes: true})) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(path);
    else yield path;
  }
}

let files = 0;
let raw = 0;
let brotli = 0;
for (const path of walk(root)) {
  if (!COMPRESSIBLE.test(path) || statSync(path).size < MIN_BYTES) continue;
  const source = readFileSync(path);
  const br = brotliCompressSync(source, {
    params: {
      [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
      [constants.BROTLI_PARAM_SIZE_HINT]: source.length,
    },
  });
  writeFileSync(`${path}.br`, br);
  writeFileSync(`${path}.gz`, gzipSync(source, {level: GZIP_LEVEL}));
  files += 1;
  raw += source.length;
  brotli += br.length;
}
console.log(`precompressed ${files} files: ${raw} B -> ${brotli} B brotli`);
