import { spawnSync } from 'child_process';
import path from 'path';
import { beforeAll, describe, expect, it } from 'vitest';

const projectRoot = path.resolve(__dirname, '..');
const entry = path.join(projectRoot, 'dist', 'index.js');

describe('upload CLI (built dist/)', () => {
  beforeAll(() => {
    const build = spawnSync('npm', ['run', 'build'], {
      cwd: projectRoot,
      encoding: 'utf-8',
    });
    if (build.status !== 0) {
      throw new Error(`Build failed: ${build.stderr || build.stdout}`);
    }
  });

  it('exits with code 1 and prints file not found for missing file', () => {
    const missingPath = path.join(
      projectRoot,
      'no-such-invoice-xyz-9999.pdf'
    );
    const r = spawnSync(process.execPath, [
      entry,
      'upload',
      '1',
      missingPath,
    ], {
      encoding: 'utf-8',
    });

    expect(r.status).toBe(1);
    expect(r.stdout).toMatch(/File not found/i);
  });
});
