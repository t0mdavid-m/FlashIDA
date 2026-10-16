#!/usr/bin/env node
// Renders the method.json key documentation (ADR-0058) from method.schema.json and the exemplar
// configs. Node built-ins only. Usage: node build.js [--draft] [--out <dir>]
'use strict';
const fs = require('fs');
const path = require('path');

const SITE_DIR = __dirname;
const REPO = path.resolve(SITE_DIR, '..', '..');
const DEF = '#/$defs/';

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const inline = (s) => esc(s).replace(/`([^`]+)`/g, '<code>$1</code>');
const get = (obj, dotted) => (dotted ? dotted.split('.') : []).reduce((o, k) => (o == null ? undefined : o[k]), obj);
const defName = (ref) => (ref && ref.startsWith(DEF) ? ref.slice(DEF.length) : null);
const has = (o, k) => Object.prototype.hasOwnProperty.call(o, k);

const TYPE = { boolean: 'bool', integer: 'int', number: 'double', string: 'string', object: 'object' };
// A nullable key is typed ["object", "null"]; its base type is the non-null one.
const baseType = (n) => (Array.isArray(n.type) ? n.type.find((t) => t !== 'null') : n.type);
const nullable = (n) => Array.isArray(n.type) && n.type.includes('null');
function typeText(n) {
  if (n['x-type-text']) return n['x-type-text'];
  let t;
  const bt = baseType(n);
  if (bt === 'array') t = 'array of ' + (n.items && n.items.properties ? 'objects' : typeText(n.items || {}));
  else if (bt === 'object' && n.additionalProperties && typeof n.additionalProperties === 'object' && !n.properties)
    t = 'map of name → ' + typeText(n.additionalProperties);
  else t = TYPE[bt] || (n.$ref ? 'object' : '?');
  if (nullable(n)) t += ' or null';
  return n['x-unit'] ? t + ', ' + n['x-unit'] : t;
}

const literal = (v) => (typeof v === 'string' ? (v === '' ? '""' : v) : JSON.stringify(v));
function defaultText(n) {
  if (n['x-default-text']) return n['x-default-text'];
  return has(n, 'default') ? literal(n.default) : 'none';
}

function rulesHtml(n, draft) {
  const parts = [];
  if (Array.isArray(n.enum)) {
    if (Array.isArray(n.enumDescriptions))
      parts.push(n.enum.map((v, i) => `<code>${esc(literal(v))}</code>: ${inline(n.enumDescriptions[i] || '')}`).join(' '));
    else parts.push('One of ' + n.enum.map((v) => `<code>${esc(literal(v))}</code>`).join(', ') + '.');
  }
  if (n.description) parts.push(inline(n.description));
  else if (draft) parts.push('<span class="missing">MISSING</span>');
  if (Array.isArray(n['x-adr']) && n['x-adr'].length) parts.push(`<span class="adr">ADR-${n['x-adr'].join(', ADR-')}</span>`);
  return parts.join(' ');
}

function exemplarCell(value, node) {
  if (value === undefined) {
    if (node['x-default-text']) return '– = ' + node['x-default-text'];
    return has(node, 'default') ? '– = ' + literal(node.default) : '–';
  }
  if (value && typeof value === 'object' && !Array.isArray(value)) return `set (${Object.keys(value).length} keys)`;
  const s = literal(value);
  return s.length > 80 ? s.slice(0, 79) + '…' : s;
}

function pageOfDef(schema, name) {
  for (const p of schema['x-pages'] || []) for (const s of p.sections) if (s.ref === DEF + name) return p.slug + '.html';
  return null;
}

// Rows of one section: nested plain objects flatten to dotted keys behind a group row; a $ref or a
// map of $ref objects is one linking row; an array of objects is a row plus one row per item key
// ("conditions[].name").
function sectionRows(node, schema, exclude, prefix = '') {
  const rows = [];
  for (const [k, n] of Object.entries(node.properties || {})) {
    if (!prefix && exclude.includes(k)) continue;
    const key = prefix ? prefix + '.' + k : k;
    const target = defName(n.$ref) || (n.additionalProperties && defName(n.additionalProperties.$ref));
    if (target) { rows.push({ key, node: n, link: pageOfDef(schema, target) }); continue; }
    if (baseType(n) === 'object' && n.properties) {
      rows.push({ key, node: n, group: true }); // every key node is documented, as the C# test requires
      rows.push(...sectionRows(n, schema, [], key));
      continue;
    }
    rows.push({ key, node: n });
    if (baseType(n) === 'array' && n.items && n.items.properties) rows.push(...sectionRows(n.items, schema, [], key + '[]'));
  }
  return rows;
}

// For a $def page: key -> "ms1, ms2, ..." naming the users that admit it, or null when every user
// admits every key (the column would say nothing).
function applicability(schema, name) {
  const users = [];
  const walk = (node, p) => {
    if (!node || typeof node !== 'object') return;
    if (node.$ref === DEF + name) users.push({ label: p.split('.').pop(), admitted: node.propertyNames && node.propertyNames.enum });
    if (node.additionalProperties && node.additionalProperties.$ref === DEF + name) users.push({ label: p.split('.').pop() + '.*', admitted: null });
    for (const [k, v] of Object.entries(node.properties || {})) walk(v, p ? p + '.' + k : k);
  };
  walk(schema, '');
  if (!users.some((u) => u.admitted)) return null;
  const col = {};
  for (const k of Object.keys(schema.$defs[name].properties))
    col[k] = users.filter((u) => !u.admitted || u.admitted.includes(k)).map((u) => u.label).join(', ');
  return col;
}

function sectionNode(schema, ref) {
  const d = defName(ref);
  if (d) {
    if (!schema.$defs || !schema.$defs[d]) throw new Error(`x-pages: section ref "${ref}" does not resolve`);
    return schema.$defs[d];
  }
  let n = schema;
  for (const p of ref.split('.')) n = n && n.properties && n.properties[p];
  if (!n) throw new Error(`x-pages: section ref "${ref}" does not resolve`);
  return n;
}

function renderSection(schema, s, ctx) {
  const node = sectionNode(schema, s.ref);
  const isLeaf = !node.properties;
  const rows = isLeaf ? [{ key: s.ref.split('.').pop(), node, leaf: true }] : sectionRows(node, schema, s.exclude || []);
  const exemplars = (s.exemplars || []).map((e) => {
    const file = path.join(ctx.configDir, e.config);
    if (!fs.existsSync(file)) throw new Error(`x-pages: exemplar ${e.config} not found`);
    const base = e.base !== undefined ? e.base : (defName(s.ref) ? null : s.ref);
    if (base === null) throw new Error(`x-pages: ${s.ref} exemplar ${e.config} needs a base`);
    // A column whose own object admits only some keys (ms1's propertyNames) shows n/a for the rest.
    let at = schema;
    for (const p of base.split('.')) at = at && at.properties && at.properties[p];
    const admitted = at && at.propertyNames && at.propertyNames.enum;
    return { label: e.label || e.config, config: JSON.parse(fs.readFileSync(file, 'utf8')), base, admitted };
  });
  const appl = defName(s.ref) ? applicability(schema, defName(s.ref)) : null;
  const head = ['key', 'type', 'default', 'values and rules'].concat(appl ? ['used by'] : [], exemplars.map((e) => e.label));
  const where = defName(s.ref) ? '$defs.' + defName(s.ref) : s.ref;
  if (!isLeaf && !defName(s.ref) && !node.description) ctx.missing.push(`${ctx.slug}: ${where}`);
  const body = rows.map((r) => {
    if (!r.node.description) ctx.missing.push(`${ctx.slug}: ${r.leaf ? where : where + '.' + r.key}`);
    const more = r.link ? ` See <a href="${r.link}">${esc(path.basename(r.link, '.html').replace(/-/g, ' '))}</a>.` : '';
    const cells = [
      `<td class="key"><code>${esc(r.key)}</code></td>`,
      `<td>${esc(typeText(r.node))}</td>`,
      `<td>${inline(defaultText(r.node))}</td>`,
      `<td>${rulesHtml(r.node, ctx.draft)}${more}</td>`]
      .concat(appl ? [`<td>${esc(appl[r.key.split('.')[0]] || '')}</td>`] : [])
      .concat(exemplars.map((e) => {
        if (r.group || r.key.includes('[]')) return '<td></td>';
        if (e.admitted && !e.admitted.includes(r.key.split('.')[0])) return '<td class="ex">n/a</td>';
        const v = r.leaf ? get(e.config, e.base) : get(e.config, e.base ? e.base + '.' + r.key : r.key);
        return `<td class="ex"><code>${esc(exemplarCell(v, r.node))}</code></td>`;
      }));
    return `<tr${r.group ? ' class="group"' : ''}>${cells.join('')}</tr>`;
  });
  const adr = s.adr && s.adr.length ? ` <span class="adr">ADR-${s.adr.join(', ADR-')}</span>` : '';
  const intro = !isLeaf && node.description ? `<p class="intro">${inline(node.description)}</p>` : '';
  return `<h2><code>${esc(s.heading || s.ref.replace(DEF, ''))}</code>${adr}</h2>${intro}
<div class="wrap"><table><thead><tr>${head.map((h) => `<th>${esc(h)}</th>`).join('')}</tr></thead>
<tbody>${body.join('\n')}</tbody></table></div>`;
}

function stamp() {
  const { SITE_DATE, SITE_COMMIT, SITE_BRANCH } = process.env;
  const state = SITE_COMMIT ? `Engine state of ${SITE_DATE}, commit ${SITE_COMMIT.slice(0, 10)} on ${SITE_BRANCH}.` : 'Working tree, uncommitted.';
  return `${state} Last column(s): the value in the named config under <code>FlashIDA/test-data/configs</code>; – means the key is absent there and the default applies.`;
}

function shell(title, nav, main) {
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)}</title><link rel="stylesheet" href="style.css"></head>
<body>${nav}<main>${main}</main></body></html>
`;
}

function build({ schema, configDir, out, draft }) {
  const pages = schema['x-pages'] || [];
  if (!pages.length) throw new Error('x-pages is empty');
  const missing = [];
  fs.mkdirSync(out, { recursive: true });
  const nav = `<nav><a href="index.html">FLASHIda method.json keys</a>${pages.map((p) => ` · <a href="${p.slug}.html">${esc(p.title)}</a>`).join('')} · <a href="print.html">print</a></nav>`;
  const bodies = pages.map((p) => {
    const ctx = { configDir, draft, missing, slug: p.slug };
    const sections = p.sections.map((s) => renderSection(schema, s, ctx)).join('\n');
    return `<h1>FLASHIda ${esc(p.title.toLowerCase())} <span class="sub">${inline(p.subtitle || '')}</span></h1>
<p class="stamp">${stamp()}</p>
${sections}
<p class="source">Read from ${inline(p.sources || '')}.</p>`;
  });
  if (missing.length && !draft) throw new Error('Keys without a description:\n  ' + missing.join('\n  '));
  pages.forEach((p, i) => fs.writeFileSync(path.join(out, p.slug + '.html'), shell(`FLASHIda keys: ${p.title}`, nav, bodies[i])));
  const index = `<h1>${esc(schema.title || 'FLASHIda method.json')} <span class="sub">every key, by section</span></h1>
<p class="stamp">${stamp()}</p><p>${inline(schema.description || '')}</p>
<ul class="pages">${pages.map((p) => `<li><a href="${p.slug}.html">${esc(p.title)}</a>: ${inline(p.subtitle || '')}</li>`).join('')}</ul>
<p>The schema itself: <a href="method.schema.json">method.schema.json</a>. One document for printing: <a href="print.html">print</a>.</p>`;
  fs.writeFileSync(path.join(out, 'index.html'), shell('FLASHIda method.json keys', nav, index));
  fs.writeFileSync(path.join(out, 'print.html'), shell('FLASHIda method.json keys (print)', '', bodies.map((x) => `<section class="page">${x}</section>`).join('\n')));
  fs.copyFileSync(path.join(SITE_DIR, 'style.css'), path.join(out, 'style.css'));
  fs.writeFileSync(path.join(out, 'method.schema.json'), JSON.stringify(schema, null, 2) + '\n');
  return { missing };
}

module.exports = { typeText, defaultText, rulesHtml, exemplarCell, sectionRows, applicability, build };

if (require.main === module) {
  const args = process.argv.slice(2);
  const draft = args.includes('--draft');
  const oi = args.indexOf('--out');
  const out = oi >= 0 ? path.resolve(args[oi + 1]) : path.join(SITE_DIR, '_site');
  try {
    const schema = JSON.parse(fs.readFileSync(path.join(SITE_DIR, 'method.schema.json'), 'utf8'));
    const { missing } = build({ schema, configDir: path.join(REPO, 'test-data', 'configs'), out, draft });
    if (draft) {
      const per = {};
      for (const m of missing) { const s = m.split(':')[0]; per[s] = (per[s] || 0) + 1; }
      for (const [s, n] of Object.entries(per)) console.log(`missing descriptions: ${s}: ${n}`);
    }
    console.log(`PASS: rendered to ${out}`);
  } catch (e) {
    console.error(e.message);
    console.log('FAIL: ' + e.message.split('\n')[0]);
    process.exit(1);
  }
}
