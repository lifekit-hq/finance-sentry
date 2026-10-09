// Pure parsing for scripts/build-whats-new.mjs: no file access, so the node:test spec can feed it text.

// The release-please sections the panel's "Every change" fold lists; every other section is dropped.
const KEPT_SECTIONS = new Set(['Features', 'Bug Fixes']);

// Scopes that name plumbing a person using the app never sees. An entry is dropped when every scope
// it lists is internal; a spec number ("040") is internal too.
const INTERNAL_SCOPES = new Set([
  'ci',
  'docker',
  'deploy',
  'mcp',
  'api',
  'migrations',
  'e2e',
  'qa',
  'observability',
  'backend',
  'gateway',
  'ops',
  'deps',
  'tooling',
  'auth',
  'companion',
  'agent',
  'analytics',
]);
const SPEC_NUMBER = /^\d+$/;

// Wording that marks a developer-facing entry whatever its scope.
const INTERNAL_TERMS = [
  /query filter/i,
  /rate-?limit/i,
  /\btokens?\b/i,
  /\bmigrations?\b/i,
  /\btests?\b/i,
  /shadow mode/i,
];

const MAX_VERSIONS = 6;
const OWNER_TAG = /^\(owner\)\s*|\s*\(owner\)$/i;

const VERSION_HEADING = /^## \[?(\d+\.\d+\.\d+)\]?(?:\([^)]*\))?\s*(?:\((\d{4}-\d{2}-\d{2})\))?\s*$/;
const ENTRY = /^\* (?:\*\*([^*]+?):\*\* )?(.+)$/;
const ISSUE_LINK = /\s*\(\[#\d+\]\([^)]*\)\)/g;
const HASH_LINK = /\s*\(\[[0-9a-f]{7,40}\]\([^)]*\)\)/g;
// Issue references left once links are reduced to text: "(#419 S3)", "(#695)", ", closes #704".
const BARE_ISSUE = /\s*\(#\d+[^)]*\)|,?\s*\b(?:closes|fixes|resolves)\s+#\d+/gi;
const MARKDOWN_LINK = /\[([^\]]+)\]\([^)]*\)/g;
const ENTITIES = {'&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'", '&amp;': '&'};

function decodeEntities(text) {
  return text.replace(/&(?:lt|gt|quot|#39|amp);/g, entity => ENTITIES[entity]);
}

function capitalise(text) {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** The entry's subject with issue links, commit hashes and markdown links reduced to plain text. */
export function cleanSubject(raw) {
  const text = raw
    .replace(ISSUE_LINK, '')
    .replace(HASH_LINK, '')
    .replace(MARKDOWN_LINK, '$1')
    .replace(BARE_ISSUE, '');
  return capitalise(decodeEntities(text).trim());
}

/** Whether a changelog entry (scope text may be undefined) is one a person using the app would read. */
export function isUserFacing(scope, subject) {
  if (scope !== undefined) {
    const scopes = scope.split(',').map(part => part.trim().toLowerCase());
    if (scopes.every(part => INTERNAL_SCOPES.has(part) || SPEC_NUMBER.test(part))) {
      return false;
    }
  }
  return !INTERNAL_TERMS.some(term => term.test(subject));
}

/**
 * CHANGELOG.md to `[{version, date, changes}]`, newest first, at most `MAX_VERSIONS`. `changes` are the
 * kept Features and Bug Fixes entries as plain sentences; a version with none gets an empty list.
 */
export function parseChangelog(text) {
  const versions = [];
  let current = null;
  let sectionKept = false;
  for (const line of text.split(/\r?\n/)) {
    const heading = VERSION_HEADING.exec(line);
    if (heading) {
      current = {version: heading[1], date: heading[2] ?? null, changes: []};
      versions.push(current);
      sectionKept = false;
    } else if (line.startsWith('### ')) {
      sectionKept = KEPT_SECTIONS.has(line.slice(4).trim());
    } else if (current !== null && sectionKept) {
      const entry = ENTRY.exec(line);
      if (entry) {
        const subject = cleanSubject(entry[2]);
        if (subject !== '' && isUserFacing(entry[1], subject)) {
          current.changes.push(subject);
        }
      }
    }
  }
  return versions.slice(0, MAX_VERSIONS);
}

/**
 * notes.md to a map of version to `[{text, owner}]`. A `## 1.15.0` heading opens a version and each
 * `- ` bullet under it is one line; `(owner)` at either end of a bullet marks a line for the Owner only.
 */
export function parseNotes(text) {
  const notes = new Map();
  let lines = null;
  for (const raw of text.split(/\r?\n/)) {
    const heading = /^## \[?(\d+\.\d+\.\d+)\]?\s*$/.exec(raw);
    if (heading) {
      lines = [];
      notes.set(heading[1], lines);
    } else if (lines !== null && raw.startsWith('- ')) {
      const body = raw.slice(2).trim();
      const owner = OWNER_TAG.test(body);
      const line = body.replace(OWNER_TAG, '').trim();
      if (line !== '') {
        lines.push({text: line, owner});
      }
    }
  }
  return notes;
}

/** The panel's JSON: each changelog version with the plain notes written for it. */
export function buildWhatsNew(changelog, notes) {
  const noteMap = parseNotes(notes);
  return {
    versions: parseChangelog(changelog).map(version => ({
      ...version,
      notes: noteMap.get(version.version) ?? [],
    })),
  };
}
