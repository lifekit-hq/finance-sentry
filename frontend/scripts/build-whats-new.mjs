// Bundles the panel's content at build time: ../CHANGELOG.md (filtered) plus the hand-written plain
// notes in src/whats-new/notes.md, written to public/whats-new.json. The app fetches that file when
// the "What's new" panel opens, so it stays out of the initial bundle.
// Usage: node scripts/build-whats-new.mjs   (runs before start/build via the npm pre-scripts)
import {existsSync, mkdirSync, readFileSync, writeFileSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';

import {buildWhatsNew} from './whats-new-parse.mjs';

const frontend = join(dirname(fileURLToPath(import.meta.url)), '..');
const changelogPath = join(frontend, '..', 'CHANGELOG.md');
const notesPath = join(frontend, 'src', 'whats-new', 'notes.md');
const outputPath = join(frontend, 'public', 'whats-new.json');

if (!existsSync(changelogPath)) {
  // The production image copies CHANGELOG.md next to frontend/; a missing file means that COPY went.
  console.error(`build-whats-new: ${changelogPath} not found`);
  process.exit(1);
}

const notes = existsSync(notesPath) ? readFileSync(notesPath, 'utf8') : '';
const data = buildWhatsNew(readFileSync(changelogPath, 'utf8'), notes);

mkdirSync(dirname(outputPath), {recursive: true});
writeFileSync(outputPath, `${JSON.stringify(data, null, 2)}\n`);
console.log(`build-whats-new: ${data.versions.length} versions -> public/whats-new.json`);
