#!/usr/bin/env python3
"""Builds src/DiscImageStudio.App/Localization/LibraryText.en.json.

The engine libraries (Burning, Core, ...) report status, diagnostics and errors as
Chinese text that never goes through the app's catalog. This fork translates those
strings at display time instead of editing the libraries (their tests assert the
original wording). Rules come from two places:
  * tools/library_text_en.json: hand translations of library literals, written with
    the original C# interpolation holes ({expr}) so they can be matched in output.
  * the app's own catalogs: every Chinese key that has an English translation.
Each rule is stored with numbered holes ({0}, {1}...) for LibraryText.cs.
"""
import glob, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CJK = re.compile(r'[㐀-鿿　-〿＀-￯]')
HOLE = re.compile(r'\{([^{}]+)\}')

def number_holes(zh, en):
    """Replace named holes with indices; returns None if the fragment is not self-contained."""
    names = HOLE.findall(zh)
    if HOLE.sub('', zh).count('{') or HOLE.sub('', zh).count('}'):
        return None
    index = {}
    for n in names:
        index.setdefault(n, len(index))
    def zh_sub(m): return '{%d}' % index[m.group(1)]
    def en_sub(m):
        if m.group(1) not in index: raise KeyError(m.group(1))
        return '{%d}' % index[m.group(1)]
    try:
        return HOLE.sub(zh_sub, zh), HOLE.sub(en_sub, en)
    except KeyError:
        return None

def unescape(s):
    return s.replace('\\n', '\n').replace('\\t', '\t').replace('\\"', '"').replace('\\\\', '\\')

rules = {}
for zh, en in json.load(open(os.path.join(ROOT, 'tools', 'library_text_en.json'), encoding='utf-8')).items():
    zh, en = unescape(zh), unescape(en)
    if len(zh.strip()) < 2 or not CJK.search(zh):
        continue
    numbered = number_holes(zh, en)
    if numbered and numbered[0].strip():
        rules[numbered[0]] = numbered[1]

catalog_dir = os.path.join(ROOT, 'src', 'DiscImageStudio.App', 'Localization', 'Catalogs')
for path in sorted(glob.glob(os.path.join(catalog_dir, '*.json'))):
    for key, translations in json.load(open(path, encoding='utf-8-sig')).items():
        zh = translations.get('zh-CN') or key
        en = (translations.get('en') or '').strip()
        if en and CJK.search(zh) and len(zh) >= 2 and not HOLE.sub('', zh).count('{'):
            rules.setdefault(zh, en)

# Longest first so whole sentences win over the fragments they contain.
ordered = sorted(rules.items(), key=lambda kv: -len(kv[0]))
out = os.path.join(ROOT, 'src', 'DiscImageStudio.App', 'Localization', 'LibraryText.en.json')
json.dump([{'zh': k, 'en': v} for k, v in ordered], open(out, 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)
print(f'{len(ordered)} rules -> {out}')
