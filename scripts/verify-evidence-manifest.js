#!/usr/bin/env node
// Canonical evidence-manifest verifier.
//
// Modes:
//   --git <ref> [--base <repo-relative-dir>]   hash committed Git blobs
//   --files --root <dir>                       hash exact file bytes
//
// The manifest format is "<64 lowercase hex>  <path>" (two spaces). Paths are
// relative to --base (git mode) or --root (files mode). Missing entries and
// digest mismatches fail. A mismatch that disappears under a CRLF/LF transform
// is reported as "newline-only-mismatch" and still fails: newline
// normalization is never accepted silently.
//
// Exit code 0 only when every entry matches.

'use strict';
const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const crypto = require('crypto');

const args = process.argv.slice(2);
const opt = { mode: null, ref: null, base: '', root: '', manifest: null, json: null };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === '--git') { opt.mode = 'git'; opt.ref = args[++i]; }
  else if (a === '--files') { opt.mode = 'files'; }
  else if (a === '--base') { opt.base = args[++i]; }
  else if (a === '--root') { opt.root = args[++i]; }
  else if (a === '--json') { opt.json = args[++i]; }
  else if (!opt.manifest) { opt.manifest = a; }
  else { console.error(`unknown argument ${a}`); process.exit(2); }
}
if (!opt.manifest || !opt.mode) {
  console.error('usage: verify-evidence-manifest.js <manifest> --git <ref> [--base <dir>] | --files --root <dir> [--json <out>]');
  process.exit(2);
}

const sha = b => crypto.createHash('sha256').update(b).digest('hex');
const readManifest = p => fs.readFileSync(p).toString('utf8').replace(/^\uFEFF/, '')
  .split(/\r?\n/).filter(Boolean);
const entries = [];
for (const line of readManifest(opt.manifest)) {
  const m = line.match(/^([0-9a-fA-F]{64})  (.+)$/);
  if (!m) {
    entries.push({ path: line, expected: null, status: 'malformed' });
    continue;
  }
  entries.push({ path: m[2].trim(), expected: m[1].toLowerCase(), status: null });
}

for (const e of entries) {
  if (e.status) continue;
  let bytes = null;
  try {
    if (opt.mode === 'git') {
      const p = opt.base ? `${opt.base.replace(/\/$/, '')}/${e.path}` : e.path;
      bytes = cp.execFileSync('git', ['cat-file', 'blob', `${opt.ref}:${p}`], { maxBuffer: 1 << 28 });
    } else {
      const p = path.join(opt.root, e.path);
      if (!fs.existsSync(p)) { e.status = 'missing'; continue; }
      bytes = fs.readFileSync(p);
    }
  } catch {
    e.status = 'missing';
    continue;
  }
  const actual = sha(bytes);
  if (actual === e.expected) { e.status = 'ok'; continue; }
  e.actual = actual;
  // Distinguish a newline-only ambiguity from a content change.
  const text = bytes.toString('utf8');
  const variants = [
    ['crlf', text.replace(/\r?\n/g, '\r\n')],
    ['lf', text.replace(/\r\n/g, '\n')],
  ];
  let variantMatch = null;
  for (const [name, value] of variants) {
    if (sha(Buffer.from(value)) === e.expected) { variantMatch = name; break; }
  }
  e.status = variantMatch ? 'newline-only-mismatch' : 'mismatch';
  if (variantMatch) e.matchedVariant = variantMatch;
  if (opt.mode === 'git') {
    // If the working-tree bytes match but the blob does not, make the
    // working-tree-only convention explicit.
    try {
      const p = path.join(opt.base || '.', e.path);
      if (fs.existsSync(p) && sha(fs.readFileSync(p)) === e.expected) e.status = 'working-tree-only-match';
      else if (fs.existsSync(p)) {
        const wt = fs.readFileSync(p).toString('utf8');
        if (sha(Buffer.from(wt.replace(/\r\n/g, '\n'))) === e.expected) e.status = 'working-tree-only-match';
      }
    } catch { /* ignore */ }
  }
}

const failures = entries.filter(e => e.status !== 'ok');
const summary = {
  manifest: opt.manifest,
  mode: opt.mode,
  ref: opt.ref || null,
  base: opt.base || null,
  root: opt.root || null,
  entries: entries.length,
  passed: entries.length - failures.length,
  failed: failures.length,
  statuses: entries.reduce((acc, e) => { acc[e.status] = (acc[e.status] || 0) + 1; return acc; }, {}),
  failures: failures.map(e => ({ path: e.path, status: e.status, expected: e.expected, actual: e.actual, matchedVariant: e.matchedVariant })),
};
if (opt.json) fs.writeFileSync(opt.json, JSON.stringify({ summary, entries }, null, 2) + '\n');
for (const f of summary.failures) console.log(`FAIL ${f.status}: ${f.path}${f.matchedVariant ? ` (matches only after ${f.matchedVariant} transform)` : ''}`);
console.log(`verified ${summary.passed}/${summary.entries}; failed ${summary.failed}`);
process.exit(failures.length === 0 ? 0 : 1);
