import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const root = fileURLToPath(new URL('../', import.meta.url));
const entry = join(root, 'tests/construction-stages.test.ts');
const d = mkdtempSync(join(tmpdir(), 'ng-cons-'));
try {
  execFileSync(process.execPath, [require.resolve('typescript/bin/tsc'), '--noEmit', '--target', 'ES2022',
    '--moduleResolution', 'bundler', '--types', 'node,vite/client', '--esModuleInterop', '--skipLibCheck',
    '--strict', '--resolveJsonModule', '--module', 'ESNext', entry], { cwd: root, stdio: 'inherit' });
  const viteRequire = createRequire(require.resolve('vite/package.json'));
  const { build } = await import(viteRequire.resolve('esbuild'));
  await build({ entryPoints: [entry], bundle: true, platform: 'node', format: 'cjs', outfile: join(d, 'test.cjs') });
  execFileSync(process.execPath, ['--test', join(d, 'test.cjs')], { stdio: 'inherit' });
} finally { rmSync(d, { recursive: true, force: true }) }
