import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {buildWhatsNew, cleanSubject, isUserFacing, parseChangelog, parseNotes} from './whats-new-parse.mjs';

const link = (label, url) => `([${label}](${url}))`;
const entry = (scope, subject, issue = 12) =>
  `* ${scope ? `**${scope}:** ` : ''}${subject} ${link(`#${issue}`, 'https://x/issues/1')} ${link('abc1234', 'https://x/commit/abc1234')}`;

const release = (version, date, sections) =>
  [
    `## [${version}](https://x/compare/v0...v${version}) (${date})`,
    '',
    ...Object.entries(sections).flatMap(([name, entries]) => ['', `### ${name}`, '', ...entries]),
    '',
  ].join('\n');

describe('parseChangelog', () => {
  it('keeps Features and Bug Fixes and drops the other sections', () => {
    const text = release('1.2.0', '2026-10-05', {
      Features: [entry('frontend', 'show a thing')],
      'Bug Fixes': [entry('frontend', 'fix a thing')],
      Performance: [entry('frontend', 'speed a thing up')],
      Refactoring: [entry('frontend', 'tidy a thing')],
      Documentation: [entry('frontend', 'describe a thing')],
    });

    assert.deepEqual(parseChangelog(text), [
      {version: '1.2.0', date: '2026-10-05', changes: ['Show a thing', 'Fix a thing']},
    ]);
  });

  it('drops internal scopes, spec numbers and internal terms, and keeps a mixed scope', () => {
    const text = release('1.2.0', '2026-10-05', {
      Features: [
        entry('ci', 'speed up the pipeline'),
        entry('auth,mcp', 'move the host'),
        entry('040', 'persona as code'),
        entry('frontend', 'scope alerts with a query filter'),
        entry('frontend', 'add a rate-limit banner'),
        entry('frontend', 'show the token expiry'),
        entry('bank-sync', 'run the migration sooner'),
        entry('frontend', 'cover the page with a test'),
        entry('frontend', 'start in shadow mode'),
        entry('radar,subscriptions', 'show the latest signals'),
        entry(undefined, 'a scopeless entry'),
      ],
    });

    assert.deepEqual(parseChangelog(text)[0].changes, ['Show the latest signals', 'A scopeless entry']);
  });

  it('strips issue links, commit hashes and markdown links, and decodes entities', () => {
    assert.equal(
      cleanSubject(
        `adopt [@lifekit-hq](https://github.com/lifekit-hq) copy &lt;amount&gt; over ${link('#757', 'https://x/issues/757')} ${link('#764', 'https://x/issues/764')} ${link('f36e0c9', 'https://x/commit/f36e0c9')}`
      ),
      'Adopt @lifekit-hq copy <amount> over'
    );
  });

  it('strips issue references that survive link removal', () => {
    // Shapes taken from release-please output: "([#419](url) S3)" and ", closes [#704](url)".
    assert.equal(
      cleanSubject('auto-resolve alerts ([#419](https://x/issues/419) S3) ([#701](https://x/issues/701)) ([bffd437](https://x/commit/bffd437))'),
      'Auto-resolve alerts'
    );
    assert.equal(
      cleanSubject('let promotions skip the floor ([#728](https://x/issues/728)) ([ef8b715](https://x/commit/ef8b715)), closes [#704](https://x/issues/704)'),
      'Let promotions skip the floor'
    );
  });

  it('keeps a version with no kept entries and reads the heading without a link', () => {
    const text = ['## 0.9.0 (2026-01-02)', '', '### Documentation', '', entry('docs', 'words'), ''].join('\n');

    assert.deepEqual(parseChangelog(text), [{version: '0.9.0', date: '2026-01-02', changes: []}]);
  });

  it('keeps the newest six versions in changelog order', () => {
    const text = [8, 7, 6, 5, 4, 3, 2, 1]
      .map(minor => release(`1.${minor}.0`, '2026-10-05', {Features: [entry('frontend', `item ${minor}`)]}))
      .join('\n');

    const versions = parseChangelog(text).map(v => v.version);

    assert.deepEqual(versions, ['1.8.0', '1.7.0', '1.6.0', '1.5.0', '1.4.0', '1.3.0']);
  });
});

describe('isUserFacing', () => {
  it('matches whole words only', () => {
    assert.equal(isUserFacing('frontend', 'show the latest contest'), true);
    assert.equal(isUserFacing('frontend', 'add a test'), false);
  });
});

describe('parseNotes', () => {
  const text = [
    '<!-- ignored -->',
    '',
    '## 1.2.0',
    '',
    '- You can do a thing.',
    '- You can invite people. (owner)',
    '- (owner) You can see the ledger.',
    '',
    '## 1.1.0',
    '- Older line.',
    'not a bullet',
  ].join('\n');

  it('reads bullets per version and tags owner lines at either end', () => {
    const notes = parseNotes(text);

    assert.deepEqual(notes.get('1.2.0'), [
      {text: 'You can do a thing.', owner: false},
      {text: 'You can invite people.', owner: true},
      {text: 'You can see the ledger.', owner: true},
    ]);
    assert.deepEqual(notes.get('1.1.0'), [{text: 'Older line.', owner: false}]);
  });

  it('is empty for an empty file', () => {
    assert.equal(parseNotes('').size, 0);
  });
});

describe('buildWhatsNew', () => {
  const changelog = [
    release('1.2.0', '2026-10-05', {Features: [entry('frontend', 'show a thing')]}),
    release('1.1.0', '2026-09-29', {Features: [entry('frontend', 'show another thing')]}),
  ].join('\n');

  it('attaches the plain notes written for each version', () => {
    const data = buildWhatsNew(changelog, '## 1.2.0\n- You can do a thing.\n');

    assert.deepEqual(data.versions[0].notes, [{text: 'You can do a thing.', owner: false}]);
    assert.deepEqual(data.versions[0].changes, ['Show a thing']);
  });

  it('gives a version without notes an empty list, and ignores notes for an unreleased version', () => {
    const data = buildWhatsNew(changelog, '## 9.9.9\n- Not released yet.\n');

    assert.deepEqual(
      data.versions.map(v => [v.version, v.notes.length]),
      [
        ['1.2.0', 0],
        ['1.1.0', 0],
      ]
    );
  });
});
