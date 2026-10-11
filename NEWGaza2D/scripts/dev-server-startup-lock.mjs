import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { open } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

/** Serialize port takeover THROUGH binding, not just the ownership check.
 * The OS releases the lock on exit; closing the parent's stdin also releases
 * it if startup crashes. Do not unlink the file: contenders share its inode. */
export async function withGameStartupLock(root, port, start) {
  const key = createHash('sha256').update(`${resolve(root)}:${port}`).digest('hex');
  const path = join(tmpdir(), `newgaza-start-${process.getuid()}-${key}.lock`);
  const file = await open(path, 'a', 0o600);
  await file.close();
  const lock = spawn('flock', ['--exclusive', '--timeout', '30', path,
    process.execPath, '-e',
    'process.stdout.write("locked\\n");process.stdin.resume();process.stdin.on("end",()=>process.exit(0));'],
    { stdio: ['pipe', 'pipe', 'pipe'] });
  const finished = new Promise(resolve => lock.once('close', resolve));
  let output = '', error = '', acquired = false;
  try {
    await new Promise((resolve, reject) => {
      lock.stdout.on('data', chunk => {
        output += chunk;
        if (output.includes('locked\n')) { acquired = true; resolve(); }
      });
      lock.stderr.on('data', chunk => { error = (error + chunk).slice(-2000); });
      lock.once('error', reject);
      lock.once('close', code => {
        if (!acquired) reject(new Error(`Could not acquire game startup lock (${code}): ${error}`));
      });
    });
    return await start();
  } finally {
    lock.stdin.end();
    await finished;
  }
}
