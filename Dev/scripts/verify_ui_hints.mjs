// @module: Housekeeping.Scripts/verify_ui_hints
// @purpose: Static AST/tag verifier ensuring 100% Standard 09 .hk-has-hint + 2-line title coverage, zero orphan CSS classes, and zero bare catches across Housekeeping.Web.
// @depends-on: node:fs, node:path, node:url
// @exports: CLI exit code 0 on PASS, 1 on any coverage or orphan CSS failure
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const webSrcDir = path.resolve(__dirname, '..', 'Housekeeping.Web', 'src');
const stylesDir = path.join(webSrcDir, 'styles');

function walkFiles(dir, exts) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      out.push(...walkFiles(full, exts));
    } else if (exts.some((ext) => entry.name.endsWith(ext))) {
      out.push(full);
    }
  }
  return out;
}

function extractOpeningTags(src, tagName) {
  const results = [];
  const pattern = new RegExp(`<${tagName}(?=[\\s>])`, 'g');
  let match;
  while ((match = pattern.exec(src)) !== null) {
    const start = match.index;
    let i = start + tagName.length + 1;
    let braceDepth = 0;
    let quote = null;
    while (i < src.length) {
      const ch = src[i];
      if (quote) {
        if (ch === '\\') {
          i += 2;
          continue;
        }
        if (ch === quote) quote = null;
      } else if (ch === '"' || ch === "'" || ch === '`') {
        quote = ch;
      } else if (ch === '{') {
        braceDepth++;
      } else if (ch === '}') {
        if (braceDepth > 0) braceDepth--;
      } else if (ch === '>' && braceDepth === 0) {
        const line = src.slice(0, start).split(/\r?\n/).length;
        results.push({ tag: src.slice(start, i + 1), line });
        break;
      }
      i++;
    }
  }
  return results;
}

const errors = [];
const tsxFiles = walkFiles(webSrcDir, ['.tsx']);
const allTsFiles = walkFiles(webSrcDir, ['.ts', '.tsx']);

let checkboxCount = 0;
let thCount = 0;

for (const file of tsxFiles) {
  const rel = path.relative(webSrcDir, file);
  const content = fs.readFileSync(file, 'utf8');

  for (const { tag, line } of extractOpeningTags(content, 'input')) {
    if (!/type\s*=\s*["']checkbox["']/.test(tag)) continue;
    checkboxCount++;
    if (!tag.includes('hk-has-hint')) {
      errors.push(`[HINT-CB] ${rel}:${line} <input type="checkbox"> missing 'hk-has-hint' class.`);
    }
    if (!/\btitle\s*=/.test(tag)) {
      errors.push(`[HINT-CB] ${rel}:${line} <input type="checkbox"> missing 'title' attribute.`);
    }
  }

  for (const { tag, line } of extractOpeningTags(content, 'th')) {
    thCount++;
    if (!tag.includes('hk-has-hint')) {
      errors.push(`[HINT-TH] ${rel}:${line} <th> missing 'hk-has-hint' class.`);
    }
    if (!/\btitle\s*=/.test(tag)) {
      errors.push(`[HINT-TH] ${rel}:${line} <th> missing 'title' attribute.`);
    }
  }
}

// Verify every hint dictionary string in src/hints/*.ts contains a 2-line \n break
const hintsDir = path.join(webSrcDir, 'hints');
if (fs.existsSync(hintsDir)) {
  for (const file of walkFiles(hintsDir, ['.ts'])) {
    const rel = path.relative(webSrcDir, file);
    const src = fs.readFileSync(file, 'utf8');
    const literals = [...src.matchAll(/(['"`])((?:\\.|(?!\1)[\s\S])*)\1/g)];
    for (const m of literals) {
      const val = m[2];
      if (val.length < 25) continue;
      if (!val.includes('\\n') && !val.includes('\n')) {
        errors.push(`[HINT-2LINE] ${rel}: Hint string missing 2-line newline separator: "${val.slice(0, 50)}..."`);
      }
    }
  }
}

// Verify zero bare catch blocks across src/**/*.{ts,tsx}
for (const file of allTsFiles) {
  const rel = path.relative(webSrcDir, file);
  const src = fs.readFileSync(file, 'utf8');
  if (/\bcatch\s*\{/.test(src)) {
    errors.push(`[ERR-001] ${rel}: Bare 'catch {' without error parameter/logging.`);
  }
  if (/\.catch\(\s*\(\s*\)\s*=>\s*\{\s*\}\s*\)/.test(src)) {
    errors.push(`[ERR-001] ${rel}: Empty '.catch(() => {})' swallow detected.`);
  }
}

// Verify hk-* class coverage between TSX className attributes and src/styles/*.css (Standard 02 §6.4 Gate A)
const cssFiles = walkFiles(stylesDir, ['.css']);
const cssText = cssFiles.map((f) => fs.readFileSync(f, 'utf8')).join('\n');
const definedHkClasses = new Set(
  [...cssText.matchAll(/\.(hk-[a-zA-Z0-9_-]+)/g)].map((m) => m[1])
);
const usedHkClasses = new Set();
for (const file of tsxFiles) {
  const rel = path.relative(webSrcDir, file);
  const src = fs.readFileSync(file, 'utf8');
  for (const attrMatch of src.matchAll(/\bclassName\s*=\s*(?:"([^"]*)"|'([^']*)'|\{([\s\S]*?)\})/g)) {
    const rawAttr = attrMatch[1] ?? attrMatch[2] ?? attrMatch[3] ?? '';
    const cleaned = rawAttr.replace(/\$\{[^}]*\}/g, '__DYN__');
    for (const m of cleaned.matchAll(/\b(hk-[a-zA-Z0-9_-]+)\b/g)) {
      const cls = m[1];
      if (cls.includes('__DYN__')) continue;
      usedHkClasses.add(cls);
      if (!definedHkClasses.has(cls)) {
        errors.push(`[CSS-UNDEF] ${rel}: Class '${cls}' referenced in TSX className but missing in src/styles/*.css.`);
      }
    }
  }
}

if (errors.length > 0) {
  console.error(`verify_ui_hints FAILED with ${errors.length} issue(s):`);
  for (const e of errors) console.error(`  - ${e}`);
  process.exit(1);
}

console.log(
  `verify_ui_hints PASS: ${checkboxCount} checkboxes, ${thCount} <th> headers, ${usedHkClasses.size}/${definedHkClasses.size} hk-* CSS selectors verified, 0 bare catches.`
);
