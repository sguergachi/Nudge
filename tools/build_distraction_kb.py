#!/usr/bin/env python3
"""Build the Distraction Knowledge Base shipped as model_exp/distraction_priors.tsv.

Dev-time repo tooling — never runs on user machines. The app ships only the TSV.

Output format (one entry per line, sorted, deduped):

    key<TAB>kind<TAB>prior

where kind is "domain" or "app" and prior is the *productive-rate* prior in [0,1]
(prior = 1 - distraction). Keys are normalized exactly like
BrowserDetector.TryNormalizeDomain (lowercase, scheme/path/port stripped, no
"www.", localhost rejected) — a mismatched key is a silent no-op at runtime, and
NudgeCrossPlatform.Tests asserts round-trip parity on every shipped domain key.

Sources, in priority order (later sources never override earlier ones):

1. CURATED  — the hand-reviewed table below: ~400 high-traffic domains and ~120
   desktop app ids, batch-labeled offline with an LLM (Claude, 2026-06) and
   reviewed by hand. This is the committed baseline and needs no network.
2. UT1      — Université de Toulouse categorized domain lists (CC BY-SA),
   https://dsi.ut-capitole.fr/blacklists/ — pass --ut1-dir to a local extract.
   Categories map to priors via CATEGORY_PRIORS.
3. TRANCO   — https://tranco-list.eu research popularity list; pass --tranco-csv
   to restrict UT1 enrichment to the top N domains people actually visit.

Regenerate with full enrichment (network/dev machine):

    curl -o ut1.tar.gz https://dsi.ut-capitole.fr/blacklists/download/blacklists.tar.gz
    tar xf ut1.tar.gz
    curl -o tranco.csv https://tranco-list.eu/download/<ID>/full
    python3 tools/build_distraction_kb.py --ut1-dir blacklists --tranco-csv tranco.csv \
        --top 20000 -o NudgeCrossPlatform/model_exp/distraction_priors.tsv

Regenerate curated-only (no network, what is committed today):

    python3 tools/build_distraction_kb.py -o NudgeCrossPlatform/model_exp/distraction_priors.tsv
"""

import argparse
import csv
import os
import re
import sys

# ── Category → productive-rate prior (the only hand-curated mapping, per plan §3) ──
CATEGORY_PRIORS = {
    # UT1 category name        prior  (distraction = 1 - prior)
    'social_networks':          0.10,
    'games':                    0.08,
    'gambling':                 0.05,
    'audio-video':              0.10,
    'adult':                    0.05,
    'shopping':                 0.35,
    'press':                    0.30,
    'sports':                   0.25,
    'celebrity':                0.15,
    'dating':                   0.10,
    'forums':                   0.35,
    'webmail':                  0.75,
    'jobsearch':                0.50,
    'bank':                     0.60,
    'financial':                0.55,
}

# ── Curated domains: category blocks share a default prior; per-key overrides inline ──
# Labeled offline (LLM batch + hand review). prior = productive rate.
CURATED_DOMAINS = [
    # social — scrolling feeds
    (0.10, [
        'facebook.com', 'instagram.com', 'x.com', 'twitter.com', 'tiktok.com',
        'reddit.com', 'old.reddit.com', 'snapchat.com', 'pinterest.com', 'tumblr.com',
        'threads.net', 'bsky.app', 'mastodon.social', 'vk.com', 'weibo.com',
        '9gag.com', 'imgur.com', 'ifunny.co', 'knowyourmeme.com', 'boredpanda.com',
        'buzzfeed.com', 'tmz.com', 'eonline.com', 'dailymail.co.uk',
    ]),
    # video / streaming
    (0.08, [
        'youtube.com', 'm.youtube.com', 'netflix.com', 'hulu.com', 'disneyplus.com',
        'max.com', 'primevideo.com', 'twitch.tv', 'dailymotion.com', 'crunchyroll.com',
        'peacocktv.com', 'paramountplus.com', 'kick.com', 'rumble.com', 'bilibili.com',
        'nicovideo.jp', 'hotstar.com', 'youku.com', 'iqiyi.com', 'stremio.com',
    ]),
    # games
    (0.08, [
        'steampowered.com', 'store.steampowered.com', 'steamcommunity.com',
        'epicgames.com', 'roblox.com', 'minecraft.net', 'ea.com', 'ubisoft.com',
        'blizzard.com', 'battle.net', 'riotgames.com', 'leagueoflegends.com', 'op.gg',
        'miniclip.com', 'poki.com', 'crazygames.com', 'coolmathgames.com',
        'nexusmods.com', 'ign.com', 'gamespot.com', 'kotaku.com', 'polygon.com',
        'pcgamer.com', 'rockpapershotgun.com',
    ]),
    # gambling
    (0.05, [
        'bet365.com', 'draftkings.com', 'fanduel.com', 'pokerstars.com', 'stake.com',
        'bovada.lv', 'betway.com', 'williamhill.com', '888casino.com', 'betmgm.com',
    ]),
    # adult
    (0.05, [
        'pornhub.com', 'xvideos.com', 'xnxx.com', 'onlyfans.com', 'chaturbate.com',
    ]),
    # entertainment lookups — less compulsive than feeds
    (0.20, [
        'chess.com', 'lichess.org', 'letterboxd.com', 'fandom.com', 'giphy.com',
        'genius.com', 'last.fm', 'myanimelist.net', 'imdb.com', 'rottentomatoes.com',
    ]),
    # music — often background while working
    (0.30, [
        'spotify.com', 'open.spotify.com', 'soundcloud.com', 'music.youtube.com',
        'pandora.com', 'deezer.com', 'tidal.com', 'bandcamp.com',
    ]),
    # shopping
    (0.35, [
        'amazon.com', 'ebay.com', 'aliexpress.com', 'etsy.com', 'walmart.com',
        'target.com', 'bestbuy.com', 'temu.com', 'shein.com', 'wish.com',
        'wayfair.com', 'ikea.com', 'costco.com', 'craigslist.org', 'mercadolibre.com',
        'rakuten.co.jp', 'flipkart.com', 'zalando.com', 'asos.com', 'newegg.com',
    ]),
    # news
    (0.30, [
        'cnn.com', 'bbc.com', 'bbc.co.uk', 'nytimes.com', 'theguardian.com',
        'foxnews.com', 'reuters.com', 'apnews.com', 'washingtonpost.com',
        'forbes.com', 'businessinsider.com', 'huffpost.com', 'axios.com',
        'politico.com', 'vice.com', 'vox.com', 'theatlantic.com', 'newyorker.com',
        'news.google.com', 'news.yahoo.com', 'drudgereport.com', 'nypost.com',
        'usatoday.com', 'nbcnews.com', 'abcnews.go.com', 'cbsnews.com', 'sky.com',
    ]),
    # sports
    (0.25, [
        'espn.com', 'nba.com', 'nfl.com', 'mlb.com', 'fifa.com', 'skysports.com',
        'bleacherreport.com', 'goal.com', 'cricbuzz.com', 'espncricinfo.com',
        'formula1.com', 'ufc.com', 'flashscore.com', 'livescore.com',
    ]),
    # finance — mostly fine, trading/crypto tickers are compulsive
    (0.60, [
        'paypal.com', 'chase.com', 'bankofamerica.com', 'wellsfargo.com',
        'fidelity.com', 'schwab.com', 'vanguard.com', 'mint.com', 'wise.com',
    ]),
    (0.25, [
        'robinhood.com', 'coinbase.com', 'coinmarketcap.com', 'coingecko.com',
        'binance.com', 'tradingview.com', 'investing.com', 'finance.yahoo.com',
    ]),
    # travel / food / personal errands
    (0.35, [
        'booking.com', 'airbnb.com', 'expedia.com', 'tripadvisor.com', 'kayak.com',
        'skyscanner.com', 'doordash.com', 'ubereats.com', 'grubhub.com', 'yelp.com',
        'zillow.com', 'realtor.com', 'autotrader.com',
    ]),
    # work-adjacent browsing — half-work half-procrastination
    (0.50, [
        'news.ycombinator.com', 'lobste.rs', 'medium.com', 'quora.com',
        'producthunt.com', 'indeed.com', 'glassdoor.com', 'linkedin.com',
        'substack.com', 'slashdot.org', 'techcrunch.com', 'theverge.com',
        'arstechnica.com', 'wired.com', 'engadget.com', 'tomshardware.com',
        'weather.com', 'accuweather.com', 'archive.org',
    ]),
    # messaging on the web
    (0.30, [
        'web.whatsapp.com', 'whatsapp.com', 'web.telegram.org', 'telegram.org',
        'messenger.com', 'discord.com', 'discordapp.com',
    ]),
    # webmail / calendars — usually work
    (0.75, [
        'gmail.com', 'mail.google.com', 'calendar.google.com', 'outlook.com',
        'outlook.live.com', 'outlook.office.com', 'mail.yahoo.com', 'proton.me',
        'protonmail.com', 'fastmail.com', 'hey.com', 'icloud.com',
    ]),
    # meetings / team chat
    (0.60, [
        'meet.google.com', 'teams.microsoft.com', 'webex.com', 'whereby.com',
    ]),
    (0.55, ['slack.com', 'app.slack.com', 'zoom.us']),
    # development
    (0.90, [
        'github.com', 'gist.github.com', 'gitlab.com', 'bitbucket.org',
        'stackoverflow.com', 'stackexchange.com', 'superuser.com', 'serverfault.com',
        'askubuntu.com', 'developer.mozilla.org', 'docs.python.org', 'pypi.org',
        'npmjs.com', 'crates.io', 'pkg.go.dev', 'go.dev', 'rust-lang.org',
        'python.org', 'nodejs.org', 'learn.microsoft.com', 'docs.microsoft.com',
        'devdocs.io', 'readthedocs.io', 'readthedocs.org', 'jsfiddle.net',
        'codepen.io', 'codesandbox.io', 'stackblitz.com', 'replit.com',
        'regex101.com', 'godbolt.org', 'sourcegraph.com', 'git-scm.com',
        'kubernetes.io', 'docker.com', 'hub.docker.com', 'terraform.io',
        'grafana.com', 'prometheus.io', 'elastic.co', 'mongodb.com',
        'postgresql.org', 'mysql.com', 'redis.io', 'sqlite.org', 'nginx.org',
        'apache.org', 'kernel.org', 'gnu.org', 'jetbrains.com',
        'code.visualstudio.com', 'neovim.io', 'huggingface.co',
        'paperswithcode.com', 'kaggle.com',
    ]),
    (0.80, [
        'leetcode.com', 'hackerrank.com', 'geeksforgeeks.org', 'w3schools.com',
        'freecodecamp.org', 'codecademy.com', 'exercism.org',
    ]),
    (0.70, [
        'dev.to', 'hashnode.com', 'chatgpt.com', 'chat.openai.com', 'claude.ai',
        'gemini.google.com', 'perplexity.ai', 'openai.com', 'anthropic.com',
        'oracle.com', 'translate.google.com', 'deepl.com',
    ]),
    # cloud consoles / infra
    (0.85, [
        'aws.amazon.com', 'console.aws.amazon.com', 'portal.azure.com',
        'azure.microsoft.com', 'cloud.google.com', 'console.cloud.google.com',
        'vercel.com', 'netlify.com', 'heroku.com', 'digitalocean.com',
        'cloudflare.com', 'linode.com', 'fly.io', 'render.com', 'supabase.com',
        'firebase.google.com', 'datadoghq.com', 'sentry.io', 'pagerduty.com',
    ]),
    # office / productivity suites
    (0.85, [
        'docs.google.com', 'sheets.google.com', 'slides.google.com',
        'drive.google.com', 'notion.so', 'airtable.com', 'trello.com', 'asana.com',
        'monday.com', 'clickup.com', 'linear.app', 'atlassian.net', 'atlassian.com',
        'basecamp.com', 'todoist.com', 'evernote.com', 'office.com',
        'microsoft365.com', 'sharepoint.com', 'dropbox.com', 'box.com',
        'figma.com', 'miro.com', 'lucidchart.com', 'overleaf.com', 'grammarly.com',
        'zotero.org', 'coda.io', 'smartsheet.com', 'docusign.com',
        'salesforce.com', 'hubspot.com', 'zendesk.com', 'intercom.com',
    ]),
    (0.70, ['canva.com', 'notion.site', 'wolframalpha.com', 'britannica.com']),
    # education / reference
    (0.70, [
        'wikipedia.org', 'en.wikipedia.org', 'wiktionary.org', 'quizlet.com',
        'merriam-webster.com', 'dictionary.com', 'thesaurus.com',
    ]),
    (0.80, [
        'coursera.org', 'udemy.com', 'edx.org', 'khanacademy.org',
        'scholar.google.com', 'jstor.org', 'arxiv.org', 'desmos.com',
        'pluralsight.com', 'brilliant.org',
    ]),
    (0.60, ['duolingo.com', 'goodreads.com']),
]

# ── Curated desktop app ids (Linux app ids / lowercase process names).
# Browsers are deliberately absent: the domain prior carries the signal there,
# and an app-level penalty would double-count against work-in-browser.
CURATED_APPS = [
    # editors / IDEs / terminals — deep work
    (0.90, [
        'code', 'code-oss', 'vscodium', 'cursor', 'windsurf', 'zed', 'helix',
        'vim', 'nvim', 'neovim', 'emacs', 'sublime_text', 'sublime-text',
        'idea', 'intellij', 'pycharm', 'webstorm', 'clion', 'rider', 'goland',
        'phpstorm', 'rubymine', 'android-studio', 'eclipse', 'netbeans',
        'qtcreator', 'kdevelop', 'geany', 'lapce',
        'terminal', 'gnome-terminal', 'konsole', 'alacritty', 'kitty', 'wezterm',
        'foot', 'xterm', 'tilix', 'terminator', 'ghostty', 'ptyxis', 'tmux',
        'dbeaver', 'datagrip', 'postman', 'insomnia', 'jupyter', 'jupyter-lab',
        'rstudio', 'matlab', 'octave', 'spyder', 'gitkraken', 'github-desktop',
        'smartgit', 'sourcetree',
    ]),
    (0.80, [
        'wireshark', 'kate', 'notepad++', 'docker-desktop', 'virtualbox',
        'virt-manager', 'arduino', 'platformio', 'kicad', 'freecad', 'openscad',
        'scribus', 'blender', 'gimp', 'inkscape', 'darktable', 'rawtherapee',
        'libreoffice', 'libreoffice-writer', 'libreoffice-calc',
        'libreoffice-impress', 'soffice', 'winword', 'excel', 'powerpnt',
        'onenote', 'obsidian', 'joplin', 'zotero', 'xournalpp', 'anki',
    ]),
    (0.75, [
        'krita', 'kdenlive', 'shotcut', 'ardour', 'davinci-resolve', 'zathura',
        'audacity', 'handbrake', 'musescore',
    ]),
    (0.70, ['thunderbird', 'evolution', 'outlook', 'okular', 'evince', 'gedit']),
    # comms — meetings are work, chat is half-and-half
    (0.55, ['slack', 'teams', 'ms-teams', 'teams-for-linux', 'zoom', 'webex']),
    (0.40, ['skype', 'signal-desktop', 'ferdium', 'element', 'gajim']),
    (0.30, ['whatsapp', 'caprine']),
    (0.25, ['telegram-desktop', 'telegram']),
    # media / games — distraction
    (0.30, ['vlc', 'rhythmbox', 'clementine', 'strawberry', 'audacious', 'spotify']),
    (0.25, ['mpv', 'celluloid', 'totem', 'smplayer']),
    (0.20, [
        'discord', 'vesktop', 'webcord', 'qbittorrent', 'transmission',
        'transmission-gtk', 'transmission-qt', 'deluge',
    ]),
    (0.10, [
        'steam', 'lutris', 'heroic', 'retroarch', 'dolphin-emu', 'pcsx2', 'rpcs3',
        'yuzu', 'ryujinx', 'minecraft-launcher', 'epicgameslauncher', 'goggalaxy',
        'battlenet', 'riotclient', 'leagueclient', 'plex', 'jellyfinmediaplayer',
        'kodi', 'moonlight', 'parsec',
    ]),
    (0.08, ['stremio']),
]

# Mirrors BrowserDetector.IsLikelyDomain's CommonFileExtensions exclusion.
COMMON_FILE_EXTENSIONS = frozenset(
    '7z avi bmp csv doc docx gif gz jpeg jpg json md mov mp3 mp4 pdf png ppt '
    'pptx rar svg tar txt wav webp xlsx xml yaml yml zip'.split())

_DOMAIN_CHARS = re.compile(r'^[a-z0-9.-]+$')


def normalize_domain(raw):
    """Mirror BrowserDetector.TryNormalizeDomain. Return key or None if rejected."""
    value = raw.strip().lower()
    if value.startswith('https://'):
        value = value[8:]
    elif value.startswith('http://'):
        value = value[7:]
    value = value.split('/', 1)[0].split(':', 1)[0].strip()
    if value.startswith('www.'):
        value = value[4:]
    if value == 'localhost' or not (4 <= len(value) <= 100):
        return None
    if '..' in value or value.startswith('.') or value.endswith('.'):
        return None
    if not _DOMAIN_CHARS.match(value):
        return None
    if '.' not in value[1:-1]:
        return None
    if value.count('.') == 1 and value.rsplit('.', 1)[1] in COMMON_FILE_EXTENSIONS:
        return None
    return value


def load_curated():
    """Curated table → {key: prior} maps. Later duplicates never override earlier."""
    domains, apps = {}, {}
    for prior, keys in _iter_blocks(CURATED_DOMAINS):
        for raw in keys:
            key = normalize_domain(raw)
            if key is None:
                sys.exit(f'curated domain failed normalization: {raw!r}')
            domains.setdefault(key, prior)
    for prior, keys in _iter_blocks(CURATED_APPS):
        for raw in keys:
            apps.setdefault(raw.strip().lower(), prior)
    return domains, apps


def _iter_blocks(table):
    return table


def load_tranco(path, top):
    """Tranco CSV (rank,domain) → set of the top-N normalized domains."""
    keep = set()
    with open(path, newline='', encoding='utf-8') as f:
        for row in csv.reader(f):
            if len(row) < 2:
                continue
            key = normalize_domain(row[1])
            if key:
                keep.add(key)
            if len(keep) >= top:
                break
    return keep


def load_ut1(ut1_dir, restrict_to=None):
    """UT1 blacklists extract → {domain: prior} via CATEGORY_PRIORS."""
    result = {}
    for category, prior in CATEGORY_PRIORS.items():
        domains_file = os.path.join(ut1_dir, category, 'domains')
        if not os.path.isfile(domains_file):
            continue
        with open(domains_file, encoding='utf-8', errors='replace') as f:
            for line in f:
                key = normalize_domain(line)
                if key is None:
                    continue
                if restrict_to is not None and key not in restrict_to:
                    continue
                result.setdefault(key, prior)
    return result


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('-o', '--output', default='NudgeCrossPlatform/model_exp/distraction_priors.tsv')
    ap.add_argument('--tranco-csv', help='local Tranco full-list CSV (rank,domain)')
    ap.add_argument('--ut1-dir', help='local extract of the UT1 blacklists tarball')
    ap.add_argument('--top', type=int, default=20000,
                    help='cap UT1 enrichment to the Tranco top N (default 20000)')
    args = ap.parse_args()

    domains, apps = load_curated()
    curated_count = len(domains)

    if args.ut1_dir:
        restrict = load_tranco(args.tranco_csv, args.top) if args.tranco_csv else None
        for key, prior in load_ut1(args.ut1_dir, restrict).items():
            domains.setdefault(key, prior)  # curated labels win

    lines = [f'{k}\tdomain\t{v:.2f}' for k, v in domains.items()]
    lines += [f'{k}\tapp\t{v:.2f}' for k, v in apps.items()]
    lines.sort()

    with open(args.output, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# Distraction Knowledge Base — productive-rate priors (prior = 1 - distraction).\n')
        f.write('# Generated by tools/build_distraction_kb.py — do not edit by hand.\n')
        f.write('# Sources: curated LLM-labeled seed (2026-06)'
                + (', UT1/Toulouse category lists' if args.ut1_dir else '')
                + (', Tranco top list' if args.tranco_csv else '') + '.\n')
        f.write('\n'.join(lines) + '\n')

    print(f'wrote {args.output}: {len(domains)} domains '
          f'({curated_count} curated), {len(apps)} apps')


if __name__ == '__main__':
    main()
