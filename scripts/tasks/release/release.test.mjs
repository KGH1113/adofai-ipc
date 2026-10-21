import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import { changedFiles, fetchMetadata, verifyIntegrity } from './npm-release.mjs';

test('published package comparison catches changed, added, and removed shipped files', () => {
  assert.deepEqual(changedFiles({ a: 'same', b: 'new', c: 'added' }, { a: 'same', b: 'old', d: 'removed' }), ['b', 'c', 'd']);
  assert.deepEqual(changedFiles({ a: 'same' }, { a: 'same' }), []);
});

test('only registry 404 permits publishing; auth, network and malformed responses fail', async () => {
  assert.equal(await fetchMetadata('test', '1.0.0', async () => new Response('', { status: 404 })), null);
  for (const status of [401, 403, 429, 500]) {
    await assert.rejects(fetchMetadata('test', '1.0.0', async () => new Response('', { status })), /HTTP/);
  }
  await assert.rejects(fetchMetadata('test', '1.0.0', async () => { throw new Error('offline'); }), /offline/);
  await assert.rejects(fetchMetadata('test', '1.0.0', async () => Response.json({})), /Incomplete/);
});

test('downloaded tarball must match npm integrity', () => {
  const bytes = Buffer.from('package');
  verifyIntegrity(bytes, `sha512-${crypto.createHash('sha512').update(bytes).digest('base64')}`);
  assert.throws(() => verifyIntegrity(bytes, 'sha512-incorrect'), /integrity/);
});

test('documentation links remain under the public /docs mount', () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../docs/src/content/docs');
  for (const file of fs.readdirSync(root, { recursive: true }).filter(file => /\.mdx?$/.test(file))) {
    const source = fs.readFileSync(path.join(root, file), 'utf8');
    assert.doesNotMatch(source, /\]\(\/(?!\/|docs\/)/, file);
  }
});

test('branch refs do not become release tags; mismatched tags still fail', () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
  const script = path.join(root, 'scripts/tasks/validate/version.mjs');
  const env = { ...process.env, GITHUB_REF_TYPE: 'branch', GITHUB_REF_NAME: 'main', RELEASE_TAG: '' };
  execFileSync(process.execPath, [script, root], { env, stdio: 'pipe' });
  assert.throws(() => execFileSync(process.execPath, [script, root], {
    env: { ...env, GITHUB_REF_TYPE: 'tag', GITHUB_REF_NAME: 'v99.0.0' }, stdio: 'pipe',
  }));
});
