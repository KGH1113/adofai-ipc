import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export const packageNames = ['adofai-ipc', '@adofai-ipc/typespec'];
const registry = 'https://registry.npmjs.org';

export function archiveContents(archive) {
  // tar metadata and compression vary between packers; compare every shipped file instead.
  const script = `import hashlib,json,sys,tarfile
with tarfile.open(sys.argv[1], 'r:gz') as archive:
    files = {}
    for member in archive.getmembers():
        if member.isdir(): continue
        if not member.isfile() or not member.name.startswith('package/') or '..' in member.name.split('/'):
            raise ValueError('Unexpected npm archive entry')
        if member.name in files: raise ValueError('Duplicate npm archive entry')
        files[member.name] = hashlib.sha256(archive.extractfile(member).read()).hexdigest()
    print(json.dumps(files, sort_keys=True))`;
  return JSON.parse(execFileSync('python3', ['-c', script, archive], { encoding: 'utf8' }));
}

export function changedFiles(candidate, published) {
  return [...new Set([...Object.keys(candidate), ...Object.keys(published)])]
    .filter(name => candidate[name] !== published[name]).sort();
}

export async function fetchMetadata(name, version, request = fetch) {
  const response = await request(`${registry}/${encodeURIComponent(name)}/${version}`, {
    signal: AbortSignal.timeout(30_000),
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`npm registry returned HTTP ${response.status} for ${name}@${version}`);
  const metadata = await response.json();
  if (metadata.name !== name || metadata.version !== version || !metadata.dist?.tarball || !metadata.dist?.integrity) {
    throw new Error(`Incomplete registry metadata for ${name}@${version}`);
  }
  return metadata;
}

export function verifyIntegrity(bytes, integrity) {
  const checks = integrity.split(/\s+/).map(value => value.split('-', 2));
  if (!checks.some(([algorithm, digest]) => ['sha512', 'sha256', 'sha1'].includes(algorithm) &&
      crypto.createHash(algorithm).update(bytes).digest('base64') === digest)) {
    throw new Error('Published npm archive integrity does not match registry metadata.');
  }
}

export async function waitForPublication(name, version, {
  lookup = fetchMetadata,
  pause = () => new Promise(resolve => setTimeout(resolve, 5_000)),
  attempts = 36,
} = {}) {
  for (let attempt = 1; attempt <= attempts; attempt++) {
    if (await lookup(name, version)) return;
    if (attempt < attempts) {
      console.log(`Waiting for npm to make ${name}@${version} public (${attempt}/${attempts}).`);
      await pause();
    }
  }
  throw new Error(`${name}@${version} was accepted but is not public yet. Retry after npm finishes processing it.`);
}

async function plan(archive) {
  const manifest = JSON.parse(execFileSync('tar', ['-xOzf', archive, 'package/package.json'], { encoding: 'utf8' }));
  if (!packageNames.includes(manifest.name) || !/^\d+\.\d+\.\d+$/.test(manifest.version)) {
    throw new Error('Unexpected package name or non-release version.');
  }
  const metadata = await fetchMetadata(manifest.name, manifest.version);
  if (!metadata) return { ...manifest, archive, publish: true };
  const url = new URL(metadata.dist.tarball);
  if (url.origin !== registry) throw new Error('Unexpected npm tarball origin.');
  const response = await fetch(url, { signal: AbortSignal.timeout(60_000) });
  if (!response.ok) throw new Error(`npm tarball download failed: HTTP ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  verifyIntegrity(bytes, metadata.dist.integrity);
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'adofai-npm-'));
  try {
    const published = path.join(temporary, 'published.tgz');
    fs.writeFileSync(published, bytes);
    const differences = changedFiles(archiveContents(archive), archiveContents(published));
    if (differences.length) {
      throw new Error(`${manifest.name}@${manifest.version} already exists with different contents. Bump the product version before merging. Changed: ${differences.slice(0, 8).join(', ')}`);
    }
  } finally {
    fs.rmSync(temporary, { recursive: true, force: true });
  }
  return { ...manifest, archive, publish: false };
}

async function main() {
  const [mode, directory] = process.argv.slice(2);
  if (!['plan', 'publish', 'bootstrap'].includes(mode) || !directory) throw new Error('Usage: npm-release.mjs <plan|publish|bootstrap> <tarball-directory>');
  const archives = fs.readdirSync(directory).filter(file => file.endsWith('.tgz')).map(file => path.join(directory, file));
  if (archives.length !== packageNames.length) throw new Error('Expected exactly two npm package archives.');
  const plans = [];
  for (const archive of archives) plans.push(await plan(archive));
  if (new Set(plans.map(item => item.name)).size !== packageNames.length || new Set(plans.map(item => item.version)).size !== 1) {
    throw new Error('The SDK and generator must have matching versions and distinct package names.');
  }
  plans.sort((a, b) => packageNames.indexOf(a.name) - packageNames.indexOf(b.name));
  for (const item of plans) {
    console.log(`${item.name}@${item.version}: ${item.publish ? 'publish new version' : 'already published; identical files'}`);
    if (mode !== 'plan' && item.publish) {
      const args = ['publish', item.archive, '--access', 'public', '--registry', registry];
      if (mode === 'publish') args.push('--provenance');
      execFileSync('npm', args, { stdio: 'inherit' });
      await waitForPublication(item.name, item.version);
      await plan(item.archive);
    }
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
