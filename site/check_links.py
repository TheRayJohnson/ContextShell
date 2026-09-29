"""Fail if any page in the built site links to a local file that doesn't exist.

python site/check_links.py [site dir]
"""
import os
import re
import sys
import urllib.parse

site = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '_site')
broken, checked = [], 0
for dirpath, _, files in os.walk(site):
    for f in files:
        # 404.html uses absolute /ContextShell/ links, which only resolve on GitHub Pages.
        if not f.endswith('.html') or f == '404.html':
            continue
        page = os.path.join(dirpath, f)
        text = open(page, encoding='utf-8').read()
        for m in re.finditer(r'(?:href|src)="([^"]+)"', text):
            url = m.group(1)
            if url.startswith(('http:', 'https:', 'mailto:', '#', 'data:', 'javascript:')):
                continue
            path = urllib.parse.unquote(urllib.parse.urlparse(url).path)
            target = os.path.normpath(os.path.join(dirpath, path))
            if path.endswith('/') or os.path.isdir(target):
                target = os.path.join(target, 'index.html')
            checked += 1
            if not os.path.exists(target):
                broken.append(f'{os.path.relpath(page, site)} -> {url}')

print(f'Checked {checked} links, {len(broken)} broken')
for b in broken:
    print('  ' + b)
sys.exit(1 if broken else 0)
