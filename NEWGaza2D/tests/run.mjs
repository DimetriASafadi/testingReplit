import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const directory = mkdtempSync(join(tmpdir(), 'newgaza-tests-'));
try {
  execFileSync(process.execPath, [require.resolve('typescript/bin/tsc'), '--target', 'ES2022', '--module', 'commonjs',
    '--moduleResolution', 'node', '--esModuleInterop', '--skipLibCheck', '--strict', '--outDir', directory,
    'tests/engine.test.ts'], { stdio: 'inherit' });
  writeFileSync(join(directory, 'package.json'), '{"type":"commonjs"}');
  execFileSync(process.execPath, ['--test', join(directory, 'tests/engine.test.js')], { stdio: 'inherit' });
} finally { rmSync(directory, { recursive: true, force: true }) }
