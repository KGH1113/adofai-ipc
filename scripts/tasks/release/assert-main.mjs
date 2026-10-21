import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';

assert.equal(process.env.GITHUB_REF, 'refs/heads/main', 'Production releases run only on main.');
const sha = process.env.GITHUB_SHA;
assert.match(sha ?? '', /^[0-9a-f]{40}$/);
const head = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const tip = execFileSync('git', ['ls-remote', '--exit-code', 'origin', 'refs/heads/main'], { encoding: 'utf8' }).split(/\s/)[0];
assert.equal(head, sha, 'Checkout differs from the triggering commit.');
assert.equal(tip, sha, 'A newer main commit exists; release that commit instead.');
console.log(`Verified current main commit ${sha}.`);
