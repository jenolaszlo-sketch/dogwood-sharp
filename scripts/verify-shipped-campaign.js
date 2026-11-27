#!/usr/bin/env node
// Verify the Q3 remediation shipped-subject campaign.
// Usage: node verify-shipped-campaign.js <artifacts-root> <out.json>
// <artifacts-root> must contain r5r3-differential/<rid>/<tfm>/ and
// r5r3-shipped/<rid>/<tfm>/ as uploaded by the workflow.

'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const root = process.argv[2];
const out = process.argv[3];
if (!root || !out) {
  console.error('usage: verify-shipped-campaign.js <artifacts-root> <out.json>');
  process.exit(2);
}
const expected = {
  'win-x64': '77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895',
  'linux-x64': '85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6',
  'osx-arm64': '290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b',
};
const cells = ['win-x64', 'linux-x64', 'osx-arm64'].flatMap(rid => ['net8.0', 'net10.0'].map(tfm => ({ rid, tfm })));
const sha = p => crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex').toUpperCase();
const json = p => JSON.parse(fs.readFileSync(p, 'utf8'));

const results = [];
for (const { rid, tfm } of cells) {
  const obsPath = path.join(root, 'r5r3-differential', rid, tfm, 'observations-corrected-reference.json');
  const assetsPath = path.join(root, 'r5r3-shipped', rid, tfm, 'product-assets.json');
  const negPath = path.join(root, 'r5r3-shipped', rid, tfm, 'negative-control', 'negative-control.json');
  const errors = [];
  let obs, assets, neg;
  try { obs = json(obsPath); } catch (e) { errors.push('missing observations: ' + obsPath); }
  try { assets = json(assetsPath); } catch (e) { errors.push('missing product assets: ' + assetsPath); }
  try { neg = json(negPath); } catch (e) { errors.push('missing negative control: ' + negPath); }

  if (obs) {
    if (obs.summary.total !== 124) errors.push('total != 124');
    if (obs.summary.passed !== 124) errors.push('passed != 124');
    if (obs.summary.failed !== 0) errors.push('failed != 0');
    if (obs.summary.pending !== 0) errors.push('pending != 0');
    if (Object.keys(obs.familyCoverage || {}).length !== 26) errors.push('families != 26');
    if ((obs.coverageErrors || []).length !== 0) errors.push('coverage errors');
    const resident = obs.cases.find(c => c.id === 'R3-C26-05-resident');
    if (!resident || resident.verdict !== 'pass') errors.push('resident control not passing');
    if (resident && assets && resident.managed.resolvedPathSha256 !== assets.productBridge.sha256.toUpperCase()) {
      errors.push('resident image hash != product bridge hash');
    }
    if (resident && resident.managed.checks.loadedModuleMatchesVerifiedAsset !== true) errors.push('resident module mismatch');
    if (resident && resident.managed.checks.identityStable !== true) errors.push('resident identity unstable');
    if (obs.runtime.rid !== rid) errors.push('rid mismatch');
    if (!String(obs.runtime.framework).includes(tfm === 'net8.0' ? '.NET 8' : '.NET 10')) errors.push('tfm mismatch');
  }
  if (assets) {
    if (assets.rid !== rid) errors.push('asset rid mismatch');
    if (assets.tfm !== tfm) errors.push('asset tfm mismatch');
    if (assets.productBridge.cargoLockSha256 !== 'ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa') errors.push('product lock not committed lock');
    if (expected[rid] && assets.cedarSharpNative.sha256.toLowerCase() !== expected[rid]) errors.push('CedarSharp native != published package native');
  }
  if (neg) {
    if (neg.result !== 'pass') errors.push('negative control did not pass');
    if (neg.exitCode === 0) errors.push('negative control exited zero');
  }
  results.push({
    rid, tfm, errors,
    summary: obs ? obs.summary : null,
    productBridge: assets ? assets.productBridge : null,
    cedarSharpNative: assets ? assets.cedarSharpNative : null,
    resident: obs ? (obs.cases.find(c => c.id === 'R3-C26-05-resident') || null) : null,
    negativeControl: neg,
  });
}

const report = { cells: results, passed: results.filter(r => r.errors.length === 0).length, failed: results.filter(r => r.errors.length !== 0).length };
fs.writeFileSync(out, JSON.stringify(report, null, 2) + '\n');
for (const r of results) console.log(r.rid, r.tfm, r.errors.length ? 'FAIL ' + r.errors.join(' | ') : 'PASS');
console.log(`campaign: ${report.passed}/${results.length} cells passed`);
process.exit(report.failed === 0 ? 0 : 1);
