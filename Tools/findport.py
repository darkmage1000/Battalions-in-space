"""Find the phone's wireless-debugging port.

  python -I findport.py HOST              scan 30000-49999 (up to about a minute), print the first open port
  python -I findport.py HOST P1 P2 ...    check just these ports (1.5 s), print the open ones

Closed ports on the phone don't answer at all (no reset), so every probe waits for its timeout. More than ~400
probes at once on Windows starts losing real answers (600 with a 0.4 s timeout missed the port every time), so the
scan runs 400 at a time with a 1 s timeout and stops at the first hit. Ports from 37000 up go first (where Android
has picked them so far), then the rest.
"""
import asyncio, sys

host = sys.argv[1]
only = [int(p) for p in sys.argv[2:]]


async def probe(p, sem, found, stop, timeout):
    if stop.is_set():
        return
    async with sem:
        if stop.is_set():
            return
        try:
            r, w = await asyncio.wait_for(asyncio.open_connection(host, p), timeout)
            found.append(p)
            w.close()
            if not only:
                stop.set()
        except Exception:
            pass


async def main():
    found, stop = [], asyncio.Event()
    if only:
        sem = asyncio.Semaphore(50)
        await asyncio.gather(*(probe(p, sem, found, stop, 1.5) for p in only))
    else:
        order = list(range(37000, 50000)) + list(range(30000, 37000))
        sem = asyncio.Semaphore(400)
        tasks = [asyncio.ensure_future(probe(p, sem, found, stop, 1.0)) for p in order]
        everything = asyncio.ensure_future(asyncio.gather(*tasks))
        hit = asyncio.ensure_future(stop.wait())
        await asyncio.wait([everything, hit], return_when=asyncio.FIRST_COMPLETED)
        for t in tasks:
            t.cancel()
        hit.cancel()
        everything.cancel()
        try:
            await everything
        except BaseException:
            pass
    print(" ".join(map(str, sorted(found))))


asyncio.run(main())
