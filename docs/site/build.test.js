'use strict';
// Tests of the key documentation renderer (ADR-0058). Run: node --test docs/site/build.test.js
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs'); const os = require('os'); const path = require('path');
const b = require('./build.js');

const scanDefs = { scan: { type: 'object', additionalProperties: false, properties: {
  a: { type: 'integer', description: 'A.' }, b: { type: 'integer', description: 'B.' } } } };

test('typeText', () => {
  assert.equal(b.typeText({ type: 'integer', 'x-unit': 'Da' }), 'int, Da');
  assert.equal(b.typeText({ type: 'number' }), 'double');
  assert.equal(b.typeText({ type: 'array', items: { type: 'string' } }), 'array of string');
  assert.equal(b.typeText({ type: 'object', additionalProperties: { type: 'number' } }), 'map of name → double');
  assert.equal(b.typeText({ type: 'boolean', 'x-type-text': 'bool switch' }), 'bool switch');
});

test('defaultText', () => {
  assert.equal(b.defaultText({ default: 'tmt6plex' }), 'tmt6plex');
  assert.equal(b.defaultText({ default: 0.0 }), '0');
  assert.equal(b.defaultText({ default: false }), 'false');
  assert.equal(b.defaultText({}), 'none');
  assert.equal(b.defaultText({ 'x-default-text': 'required when enabled', default: 1 }), 'required when enabled');
});

test('rulesHtml', () => {
  assert.equal(b.rulesHtml({ enum: ['a', 'b'], enumDescriptions: ['one.', 'two.'], description: 'Then `x`.' }),
    '<code>a</code>: one. <code>b</code>: two. Then <code>x</code>.');
  assert.match(b.rulesHtml({ enum: ['a', 'b'], description: 'D.' }), /^One of <code>a<\/code>, <code>b<\/code>\./);
  assert.match(b.rulesHtml({ description: '<b>' }), /&lt;b&gt;/);
});

test('exemplarCell', () => {
  assert.equal(b.exemplarCell(undefined, { default: 2 }), '– = 2');
  assert.equal(b.exemplarCell(undefined, {}), '–');
  assert.equal(b.exemplarCell({ a: 1, b: 2, c: 3 }, {}), 'set (3 keys)');
  const long = b.exemplarCell(Array.from({ length: 40 }, (_, i) => i * 1000), {});
  assert.ok(long.length <= 80 && long.endsWith('…'));
  assert.equal(b.exemplarCell('tmt6plex', {}), 'tmt6plex');
});

test('sectionRows flattens nested objects and links refs', () => {
  const schema = { $defs: scanDefs, 'x-pages': [{ slug: 'scan-settings', sections: [{ ref: '#/$defs/scan' }] }] };
  const node = { properties: {
    cycle_time: { type: 'object', description: 'C.', properties: { enabled: { type: 'boolean', description: 'E.' }, value_ms: { type: 'number', description: 'V.' } } },
    ms1: { $ref: '#/$defs/scan', description: 'Survey.' } } };
  const rows = b.sectionRows(node, schema, []);
  assert.deepEqual(rows.map((r) => r.key), ['cycle_time', 'cycle_time.enabled', 'cycle_time.value_ms', 'ms1']);
  assert.ok(rows[0].group);
  assert.equal(rows[3].link, 'scan-settings.html');
});

test('applicability column appears only when keys differ by user', () => {
  const s1 = { $defs: scanDefs, properties: { ms: { type: 'object', properties: {
    ms1: { $ref: '#/$defs/scan', propertyNames: { enum: ['a'] } }, ms2: { $ref: '#/$defs/scan' } } } } };
  const col = b.applicability(s1, 'scan');
  assert.ok(col);
  assert.equal(col.b, 'ms2');
  assert.equal(col.a, 'ms1, ms2');
  const s2 = { $defs: scanDefs, properties: { x: { $ref: '#/$defs/scan' }, y: { $ref: '#/$defs/scan' } } };
  assert.equal(b.applicability(s2, 'scan'), null);
});

function fixture(withMissing) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'site-'));
  const configDir = path.join(dir, 'configs'); fs.mkdirSync(configDir);
  fs.writeFileSync(path.join(configDir, 'c.json'), JSON.stringify({ s1: { k: 3 }, s2: { m: true } }));
  const schema = {
    title: 'T', description: 'Orientation.', type: 'object', additionalProperties: false,
    properties: {
      s1: { type: 'object', additionalProperties: false, description: 'S1.', properties: { k: { type: 'integer', default: 1, ...(withMissing ? {} : { description: 'K.' }) } } },
      s2: { type: 'object', additionalProperties: false, description: 'S2.', properties: { m: { type: 'boolean', default: false, description: 'M.' } } } },
    'x-pages': [
      { slug: 'a', title: 'A', subtitle: 'keys of s1', sources: 'x', sections: [{ ref: 's1', exemplars: [{ config: 'c.json' }] }] },
      { slug: 'b', title: 'B', subtitle: 'keys of s2', sources: 'x', sections: [{ ref: 's2', exemplars: [{ config: 'c.json' }] }] }] };
  return { schema, configDir, out: path.join(dir, 'out') };
}

test('strict build fails on a missing description and names it', () => {
  assert.throws(() => b.build({ ...fixture(true), draft: false }), /s1\.k/);
  const f = fixture(true); b.build({ ...f, draft: true });
  assert.match(fs.readFileSync(path.join(f.out, 'a.html'), 'utf8'), /MISSING/);
});

test('build writes index, every page, print page', () => {
  const f = fixture(false); b.build({ ...f, draft: false });
  for (const n of ['index.html', 'a.html', 'b.html', 'print.html', 'style.css', 'method.schema.json'])
    assert.ok(fs.existsSync(path.join(f.out, n)), n);
  const index = fs.readFileSync(path.join(f.out, 'index.html'), 'utf8');
  assert.match(index, /href="a\.html"/); assert.match(index, /href="b\.html"/);
  const print = fs.readFileSync(path.join(f.out, 'print.html'), 'utf8');
  assert.match(print, /<code>k<\/code>/); assert.match(print, /<code>m<\/code>/);
});

test('every exemplar config named in the real schema exists', () => {
  const schema = JSON.parse(fs.readFileSync(path.join(__dirname, 'method.schema.json'), 'utf8'));
  const configDir = path.join(__dirname, '..', '..', 'test-data', 'configs');
  for (const p of schema['x-pages']) for (const s of p.sections) for (const e of s.exemplars || [])
    assert.ok(fs.existsSync(path.join(configDir, e.config)), `${p.slug}: ${e.config}`);
});
