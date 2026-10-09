import {mkdtemp, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

// Where the smoke keeps its signed-in session between a failed attempt and the retry. Created fresh
// for each run (mode 0700) and removed when the run ends, pass or fail, so a session cookie never
// outlives its run and a new run always signs in. Workers are forked after this runs and inherit
// the variable.
export const SESSION_DIR_ENV = 'E2E_LIVE_SESSION_DIR';

export default async function globalSetup(): Promise<() => Promise<void>> {
  const dir = await mkdtemp(join(process.env['RUNNER_TEMP'] ?? tmpdir(), 'live-smoke-'));
  process.env[SESSION_DIR_ENV] = dir;
  return async () => {
    await rm(dir, {recursive: true, force: true});
  };
}
