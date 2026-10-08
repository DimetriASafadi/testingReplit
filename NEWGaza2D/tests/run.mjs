import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const directory = mkdtempSync(join(tmpdir(), 'newgaza-tests-'));
try {
  execFileSync(process.execPath, [require.resolve('typescript/bin/tsc'), '--target', 'ES2022', '--module', 'commonjs',
    '--moduleResolution', 'node', '--resolveJsonModule', '--esModuleInterop', '--skipLibCheck', '--strict', '--outDir', directory,
    'tests/engine.test.ts', 'tests/minute-timers.test.ts', 'tests/audio-mix.test.ts', 'tests/canvas-contacts.test.ts', 'tests/city-layout.test.ts', 'tests/machinery-depth.test.ts', 'tests/district-environment.test.ts',
    'tests/building-footprint.test.ts', 'tests/sprite-opacity.test.ts', 'tests/construction-worker-motion.test.ts', 'tests/depot-motion.test.ts', 'tests/clearing-progress.test.ts', 'tests/ground-tiles.test.ts', 'tests/theme-music.test.ts'], { stdio: 'inherit' });
  writeFileSync(join(directory, 'package.json'), '{"type":"commonjs"}');
  execFileSync(process.execPath, ['--test', join(directory, 'tests/engine.test.js'), join(directory, 'tests/minute-timers.test.js'), join(directory, 'tests/audio-mix.test.js'), join(directory, 'tests/canvas-contacts.test.js'), join(directory, 'tests/city-layout.test.js'),
    join(directory, 'tests/machinery-depth.test.js'),
    join(directory, 'tests/district-environment.test.js'),
    join(directory, 'tests/building-footprint.test.js'),
    join(directory, 'tests/sprite-opacity.test.js'), join(directory, 'tests/construction-worker-motion.test.js'), join(directory, 'tests/depot-motion.test.js'), join(directory, 'tests/clearing-progress.test.js'), join(directory, 'tests/ground-tiles.test.js'), join(directory, 'tests/theme-music.test.js')], { stdio: 'inherit' });
} finally { rmSync(directory, { recursive: true, force: true }) }
