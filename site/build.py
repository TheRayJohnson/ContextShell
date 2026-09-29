"""
Builds the ContextShell website (GitHub Pages) from docs/.

docs/*.html are content fragments and docs/menu.json is the navigation, the same files the
original Nilesoft Shell docs used. This script wraps each fragment in the site layout, rewrites
"/docs/..." links to relative ones so the site works under any base path, and writes:

    site/_site/index.html               landing page
    site/_site/docs/<page>/index.html   one folder per docs page (pretty URLs)
    site/_site/assets/                  CSS, JS, search index, images

Standard library only.   python site/build.py [--out DIR]
"""
import argparse
import html
import json
import os
import re
import shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, 'docs')
SITE = os.path.join(ROOT, 'site')
REPO = 'https://github.com/TheRayJohnson/ContextShell'
LATEST = REPO + '/releases/latest'


def read(path):
    with open(path, encoding='utf-8-sig') as f:
        return f.read()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


def slug_of(link):
    """'/docs/configuration/themes' -> 'configuration/themes', '/docs' -> ''"""
    return link.strip('/')[len('docs'):].strip('/')


def flatten(menu):
    pages = []
    for entry in menu:
        pages.append((entry['title'], slug_of(entry['link']), None))
        for child in entry.get('items', []):
            pages.append((child['title'], slug_of(child['link']), entry['title']))
    return pages


def rel_root(depth):
    return '../' * depth if depth else './'


def rewrite_links(fragment, root):
    """Make /docs/... links and image sources relative to the page."""
    def fix(m):
        attr, quote, url = m.group(1), m.group(2), m.group(3)
        path, _, anchor = url.partition('#')
        path = path.rstrip('/')
        if path == '/docs':
            target = root + 'docs/'
        elif path.startswith('/docs/images/'):
            target = root + path.lstrip('/')
        else:
            target = root + path.lstrip('/') + '/'
        return f'{attr}={quote}{target}{"#" + anchor if anchor else ""}{quote}'
    return re.sub(r'(href|src)=(["\'])(/docs(?:/[^"\'#]*)?(?:#[^"\']*)?)\2', fix, fragment)


def text_of(fragment):
    t = re.sub(r'<(script|style)[^>]*>.*?</\1>', ' ', fragment, flags=re.S)
    t = re.sub(r'<[^>]+>', ' ', t)
    t = html.unescape(t)
    return re.sub(r'\s+', ' ', t).strip()


def first_heading(fragment, fallback):
    m = re.search(r'<h4[^>]*>(.*?)</h4>', fragment, flags=re.S)
    return text_of(m.group(1)) if m else fallback


def nav_html(menu, current, root):
    out = ['<nav class="sidebar-nav" aria-label="Documentation">']
    for entry in menu:
        slug = slug_of(entry['link'])
        items = entry.get('items', [])
        in_section = current == slug or any(slug_of(c['link']) == current for c in items)
        cls = ' class="active"' if current == slug else ''
        href = root + ('docs/' + slug + '/' if slug else 'docs/')
        if items:
            out.append(f'<details{" open" if in_section else ""}><summary><a href="{href}"{cls}>{html.escape(entry["title"])}</a></summary><ul>')
            for c in items:
                cs = slug_of(c['link'])
                ccls = ' class="active" aria-current="page"' if cs == current else ''
                out.append(f'<li><a href="{root}docs/{cs}/"{ccls}>{html.escape(c["title"])}</a></li>')
            out.append('</ul></details>')
        else:
            aria = ' aria-current="page"' if current == slug else ''
            out.append(f'<a class="top{" active" if current == slug else ""}" href="{href}"{aria}>{html.escape(entry["title"])}</a>')
    out.append('</nav>')
    return '\n'.join(out)


def layout(title, body, root, description, sidebar='', page_class='', edit_url=None):
    template = read(os.path.join(SITE, 'template.html'))
    return (template
            .replace('{{title}}', html.escape(title))
            .replace('{{description}}', html.escape(description))
            .replace('{{root}}', root)
            .replace('{{sidebar}}', sidebar)
            .replace('{{page_class}}', page_class)
            .replace('{{edit}}', f'<a class="edit" href="{edit_url}">Edit this page on GitHub</a>' if edit_url else '')
            .replace('{{repo}}', REPO)
            .replace('{{latest}}', LATEST)
            .replace('{{body}}', body))


def build(out):
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    menu = json.loads(read(os.path.join(DOCS, 'menu.json')))
    pages = flatten(menu)
    known = {slug for _, slug, _ in pages}

    # Every fragment on disk is published, even if it isn't in the menu.
    for dirpath, _, files in os.walk(DOCS):
        for f in files:
            if f.endswith('.html'):
                rel = os.path.relpath(os.path.join(dirpath, f), DOCS).replace(os.sep, '/')[:-5]
                slug = '' if rel == 'index' else rel[:-len('/index')] if rel.endswith('/index') else rel
                if slug not in known:
                    pages.append((None, slug, None))
                    known.add(slug)

    search = []
    ordered = [p for p in pages if p[0] is not None]
    for i, (title, slug, section) in enumerate(pages):
        src = os.path.join(DOCS, (slug or 'index') + '.html')
        if not os.path.exists(src):
            src = os.path.join(DOCS, slug, 'index.html')
        if not os.path.exists(src):
            print('missing fragment for', slug)
            continue
        fragment = read(src)
        depth = 1 + (len(slug.split('/')) if slug else 0)
        root = rel_root(depth)
        heading = first_heading(fragment, title or slug)
        title = title or heading

        body = rewrite_links(fragment, root)

        # Previous / next in menu order
        pager = ''
        if (title, slug, section) in ordered:
            k = ordered.index((title, slug, section))
            prev_ = ordered[k - 1] if k > 0 else None
            next_ = ordered[k + 1] if k + 1 < len(ordered) else None
            def link(p, label, cls):
                if not p:
                    return '<span></span>'
                href = root + ('docs/' + p[1] + '/' if p[1] else 'docs/')
                return f'<a class="{cls}" href="{href}"><small>{label}</small>{html.escape(p[0])}</a>'
            pager = f'<nav class="pager">{link(prev_, "Previous", "prev")}{link(next_, "Next", "next")}</nav>'

        crumbs = f'<div class="crumbs"><a href="{root}docs/">Docs</a>' + (f' / {html.escape(section)}' if section else '') + '</div>'
        article = f'{crumbs}<article class="doc">{body}</article>{pager}'
        edit = f'{REPO}/edit/main/docs/{(slug or "index")}.html'
        page = layout(f'{heading} · ContextShell docs', article, root,
                      text_of(fragment)[:155], sidebar=nav_html(menu, slug, root),
                      page_class='docs', edit_url=edit)
        write(os.path.join(out, 'docs', slug, 'index.html'), page)
        search.append({'t': heading, 's': section or '', 'u': 'docs/' + (slug + '/' if slug else ''), 'x': text_of(fragment)[:4000]})

    # Landing page
    landing = read(os.path.join(SITE, 'home.html')).replace('{{latest}}', LATEST).replace('{{repo}}', REPO)
    write(os.path.join(out, 'index.html'),
          layout('ContextShell: a better right-click menu for Windows', landing, './',
                 'Free, open-source context menu manager for Windows 10 and 11 File Explorer. Themes, a settings app and no bloat.',
                 page_class='home'))

    # 404 page (GitHub Pages serves /404.html). Use absolute paths since its depth is unknown.
    write(os.path.join(out, '404.html'),
          layout('Page not found · ContextShell', '<section class="notfound"><h1>Page not found</h1>'
                 '<p>That page doesn\'t exist. It may have moved when the docs did.</p>'
                 '<p><a class="button primary" href="/ContextShell/docs/">Go to the docs</a></p></section>',
                 '/ContextShell/', 'Page not found', page_class='home'))

    # Assets
    shutil.copytree(os.path.join(SITE, 'assets'), os.path.join(out, 'assets'))
    shutil.copytree(os.path.join(DOCS, 'images'), os.path.join(out, 'docs', 'images'))
    shutil.copy(os.path.join(ROOT, 'packages', 'assets', 'logo-256.png'), os.path.join(out, 'assets', 'logo.png'))
    shutil.copy(os.path.join(ROOT, 'packages', 'assets', 'logo-32.png'), os.path.join(out, 'assets', 'favicon.png'))
    shots = os.path.join(out, 'assets', 'screenshots')
    os.makedirs(shots, exist_ok=True)
    for f in os.listdir(os.path.join(ROOT, 'screenshots')):
        shutil.copy(os.path.join(ROOT, 'screenshots', f), shots)
    write(os.path.join(out, 'assets', 'search.json'), json.dumps(search, ensure_ascii=False, separators=(',', ':')))
    write(os.path.join(out, '.nojekyll'), '')
    print(f'Built {len(search)} docs pages into {out}')


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(SITE, '_site'))
    build(ap.parse_args().out)
