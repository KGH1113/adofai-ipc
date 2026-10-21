import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const docs = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const content = path.join(docs, 'src/content/docs');
const dist = path.join(docs, 'dist');
const locales = [{ prefix: '', lang: 'ko' }, { prefix: 'en/', lang: 'en' }, { prefix: 'zh-cn/', lang: 'zh-CN' }];
const files = directory => fs.readdirSync(directory, { recursive: true }).filter(file => /\.mdx?$/.test(file)).sort();
const original = files(content).filter(file => !/^(en|zh-cn)\//.test(file));

for (const locale of locales.slice(1)) {
  assert.deepEqual(files(path.join(content, locale.prefix)), original, `Missing or extra ${locale.lang} translations`);
}

const ui = JSON.parse(fs.readFileSync(path.join(docs, 'src/content/i18n/ko.json'), 'utf8'));
for (const language of ['en', 'zh-CN']) {
  const translated = JSON.parse(fs.readFileSync(path.join(docs, `src/content/i18n/${language}.json`), 'utf8'));
  assert.deepEqual(Object.keys(translated).sort(), Object.keys(ui).sort(), `Incomplete ${language} UI`);
  for (const key of Object.keys(ui)) {
    assert.deepEqual(translated[key].match(/\[[A-Z_]+\]/g)?.sort(), ui[key].match(/\[[A-Z_]+\]/g)?.sort(), `${language}: broken search placeholder ${key}`);
  }
}

const htmlFiles = fs.readdirSync(dist, { recursive: true }).filter(file => file.endsWith('.html'));
const pages = new Map(htmlFiles.map(file => {
  const html = fs.readFileSync(path.join(dist, file), 'utf8');
  return [file, { html, ids: new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1])) }];
}));
const attributes = tag => Object.fromEntries([...tag.matchAll(/([\w-]+)="([^"]*)"/g)].map(match => [match[1], match[2]]));
let links = 0;

for (const locale of locales) {
  const prefix = `/docs/${locale.prefix}`;
  for (const source of original) {
    const route = source.replace(/\.mdx?$/, '').replace(/(^|\/)index$/, '$1');
    const output = route ? `${route}/index.html` : 'index.html';
    const page = pages.get(`${locale.prefix}${output}`);
    assert.ok(page, `Missing page: ${prefix}${route}`);
    assert.equal(attributes(page.html.match(/<html\b[^>]*>/)[0]).lang, locale.lang, `Wrong language: ${prefix}${route}`);
    assert.ok(!page.html.includes('starlight__fallback'), `Untranslated fallback: ${prefix}${route}`);
    if (locale.lang !== 'ko') {
      for (const label of page.html.matchAll(/\b(?:title|aria-label)="([^"]+)"/g)) {
        assert.ok(!/[가-힣]/.test(label[1]), `Untranslated ${locale.lang} control: ${label[1]}`);
      }
    }
    const main = page.html.match(/<main\b[^>]*>([\s\S]*?)<\/main>/)?.[1] ?? '';
    for (const match of main.matchAll(/\bhref="(\/docs\/[^"#?]*)/g)) {
      if (/\.(css|js|svg|png|woff2?)$/.test(match[1])) continue;
      assert.ok(match[1].startsWith(prefix), `Link leaves ${locale.lang}: ${match[1]}`);
      if (locale.lang === 'ko') assert.ok(!/^\/docs\/(en|zh-cn)\//.test(match[1]), `Korean article links to a translation: ${match[1]}`);
    }
    const alternates = [...page.html.matchAll(/<link\b[^>]*>/g)].map(match => attributes(match[0])).filter(tag => tag.rel === 'alternate');
    for (const target of locales) {
      assert.ok(alternates.some(tag => tag.hreflang === target.lang && tag.href?.includes(`/docs/${target.prefix}`)), `Missing ${target.lang} alternate: ${prefix}${route}`);
    }
  }
  const notFound = pages.get(locale.prefix ? `${locale.prefix}404/index.html` : '404.html');
  assert.ok(notFound, `Missing ${locale.lang} 404 page`);
  assert.equal(attributes(notFound.html.match(/<html\b[^>]*>/)[0]).lang, locale.lang);
  assert.ok(notFound.html.includes('content="noindex"'), `404 page must not be indexed: ${locale.lang}`);
}

for (const [file, { html }] of pages) {
  for (const match of html.matchAll(/\b(?:href|src)="([^"<>]+)"/g)) {
    const url = new URL(match[1].replaceAll('&amp;', '&'), `https://adofai-ipc.impl1113.dev/docs/${file.replace(/index\.html$/, '')}`);
    if (url.origin !== 'https://adofai-ipc.impl1113.dev' || !url.pathname.startsWith('/docs/')) continue;
    let target = decodeURIComponent(url.pathname.slice('/docs/'.length));
    if (!target || target.endsWith('/')) target += 'index.html';
    // Astro emits the root 404 route as 404.html; Nginx serves it for /docs/404/ too.
    if (target === '404/index.html') target = '404.html';
    assert.ok(fs.existsSync(path.join(dist, target)), `${file}: missing linked file ${url.pathname}`);
    if (url.hash && pages.has(target)) assert.ok(pages.get(target).ids.has(decodeURIComponent(url.hash.slice(1))), `${file}: missing anchor ${url.href}`);
    links++;
  }
}

const search = JSON.parse(fs.readFileSync(path.join(dist, 'pagefind/pagefind-entry.json'), 'utf8'));
for (const { lang } of locales) {
  assert.equal(search.languages[lang.toLowerCase()]?.page_count, original.length, `Incomplete ${lang} search index`);
}
console.log(`Validated ${original.length} documents in ${locales.length} languages, localized 404 pages, ${links} links/assets, and all search indexes.`);
