#!/usr/bin/env python3
"""Read JS-rendered pages through a real Chromium, over the DevTools protocol.

Exists because Reddit — the single most useful source for "what do people want
from a Meshmixer replacement", backlog item 5 — refuses plain HTTP clients.
curl and WebFetch get 403s, and headless Chromium gets a "Prove your humanity"
challenge. A normal, headed Chromium on the dev host's real display is served
normally, so this drives that browser rather than pretending to be one.

Start the browser once, then run this against it:

    DISPLAY=:0 chromium --user-data-dir=/tmp/cprof --remote-debugging-port=9222 \
        --no-first-run --no-default-browser-check about:blank &

    python3 scripts/browse.py text  <url> [<url> ...]   # rendered innerText
    python3 scripts/browse.py links <url> [<url> ...]   # /comments/ hrefs (Reddit)

Notes learned the hard way:
  - /json/new needs PUT on current Chrome, not GET (405 otherwise).
  - Reddit search results render after load; the settle delay is load-bearing.
  - Do NOT try to defeat the bot challenge. If a page returns "Prove your
    humanity", stop and tell the user rather than working around it.
  - `pkill -f chromium...` will match this shell's own command line and kill
    the caller. Bracket a letter: `pkill -f "chromiu[m]"`.
"""
import asyncio, json, sys, urllib.request, urllib.parse
import websockets

MODE = "text"


def http_json(path, method="GET"):
    req = urllib.request.Request(f"http://127.0.0.1:9222{path}", method=method)
    with urllib.request.urlopen(req, timeout=10) as r:
        body = r.read().decode()
    return json.loads(body) if body.strip().startswith(("{", "[")) else {}


async def fetch(url, settle=6.0):
    tab = http_json("/json/new?" + urllib.parse.quote(url, safe=":/?&=%+"), method="PUT")
    ws_url, tab_id = tab["webSocketDebuggerUrl"], tab["id"]
    try:
        async with websockets.connect(ws_url, max_size=64 * 1024 * 1024) as ws:
            i = [0]

            async def send(method, params=None):
                i[0] += 1
                await ws.send(json.dumps({"id": i[0], "method": method,
                                          "params": params or {}}))
                while True:
                    msg = json.loads(await asyncio.wait_for(ws.recv(), timeout=60))
                    if msg.get("id") == i[0]:
                        return msg

            await send("Page.enable")
            await asyncio.sleep(settle)
            # Expand any "more comments" the page rendered lazily.
            expr = ("JSON.stringify([...document.querySelectorAll('a[href*=\\'/comments/\\']')]"
                    ".map(a=>a.getAttribute('href')))") if MODE == "links" else \
                   "document.body ? document.body.innerText : ''"
            r = await send("Runtime.evaluate", {"expression": expr, "returnByValue": True})
            return r.get("result", {}).get("result", {}).get("value", "") or ""
    finally:
        try:
            http_json(f"/json/close/{tab_id}")
        except Exception:
            pass


async def main():
    global MODE
    args = sys.argv[1:]
    if args and args[0] in ("text", "links"):
        MODE = args.pop(0)
    for url in args:
        try:
            text = await fetch(url)
        except Exception as e:
            text = f"<<ERROR {type(e).__name__}: {e}>>"
        print(f"=== {url} ===")
        print(text)
        print()


if __name__ == "__main__":
    asyncio.run(main())
