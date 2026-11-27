#!/usr/bin/env node
// Negative/positive controls for verify-evidence-manifest.js.
// Runs entirely in a temporary directory and leaves no repository changes.

'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const cp = require('child_process');
const crypto = require('crypto');

const repo = path.resolve(__dirname, '..');
const verifier = path.join(__dirname, 'verify-evidence-manifest.js');
const sha = b => crypto.createHash('sha256').update(b).digest('hex');
const results = [];

function run(name, manifest, opts, expectCode, expectStatus) {
  const json = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'q3-manifest-')), 'out.json');
  const args = [verifier, manifest, ...opts, '--json', json];
  const p = cp.spawnSync(process.execPath, args, { cwd: repo, encoding: 'utf8' });
  let statuses = {};
  if (fs.existsSync(json)) statuses = JSON.parse(fs.readFileSync(json, 'utf8')).summary.statuses;
  const ok = p.status === expectCode && (!expectStatus || statuses[expectStatus] > 0);
  results.push({ name, passed: ok, exitCode: p.status, expectedExitCode: expectCode, statuses, stdout: p.stdout.trim() });
  return ok;
}

const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'q3-manifest-selftest-'));
const lf = path.join(dir, 'lf.txt');
const crlf = path.join(dir, 'crlf.txt');
const missing = path.join(dir, 'missing.txt');
fs.writeFileSync(lf, 'hello\nworld\n');
fs.writeFileSync(crlf, 'hello\r\nworld\r\n');
const changedFile = path.join(dir, 'changed.txt');
fs.writeFileSync(changedFile, 'hello\nworld!\n');
const lfHash = sha(fs.readFileSync(lf));

function manifest(name, hash, file) {
  const p = path.join(dir, name);
  fs.writeFileSync(p, `${hash}  ${file}\n`);
  return p;
}

run('lf-exact-passes', manifest('m1', lfHash, 'lf.txt'), ['--files', '--root', dir], 0, 'ok');
run('changed-content-fails', manifest('m2', lfHash, 'changed.txt'), ['--files', '--root', dir], 1, 'mismatch');
run('missing-artifact-fails', manifest('m3', lfHash, 'missing.txt'), ['--files', '--root', dir], 1, 'missing');
run('stale-checksum-fails', manifest('m4', sha(Buffer.from('other\n')), 'lf.txt'), ['--files', '--root', dir], 1, 'mismatch');
run('crlf-is-not-silently-accepted', manifest('m5', lfHash, 'crlf.txt'), ['--files', '--root', dir], 1, 'newline-only-mismatch');

// Git mode: the 71-entry Q3 reviewer manifest must verify, and a stale copy
// must fail.
const q3Manifest = path.join(repo, 'docs/reviews/d3-qualification-01-q3/manifest.sha256');
run('q3-git-manifest-passes', q3Manifest, ['--git', 'HEAD', '--base', 'docs/reviews/d3-qualification-01-q3'], 0, 'ok');
const stale = path.join(dir, 'q3-stale.sha256');
const lines = fs.readFileSync(q3Manifest, 'utf8').trim().split(/\r?\n/);
lines[0] = lines[0].replace(/^[0-9a-f]{64}/, '0'.repeat(64));
fs.writeFileSync(stale, lines.join('\n') + '\n');
run('q3-stale-copy-fails', stale, ['--git', 'HEAD', '--base', 'docs/reviews/d3-qualification-01-q3'], 1, 'mismatch');

const passed = results.filter(r => r.passed).length;
const out = {
  controls: results.length,
  passed,
  failed: results.length - passed,
  results,
};
const outPath = path.join(repo, 'docs/reviews/d3-q3-remed-01/evidence/manifest-self-test.json');
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(out, null, 2) + '\n');
console.log(`manifest self-test: ${passed}/${results.length} passed -> ${outPath}`);
for (const r of results) if (!r.passed) console.log(`FAIL ${r.name}: exit ${r.exitCode}, statuses ${JSON.stringify(r.statuses)}`);
process.exit(passed === results.length ? 0 : 1);
