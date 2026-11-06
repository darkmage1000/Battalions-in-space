import asyncio, sys
host = sys.argv[1]
async def probe(p, sem, found):
    async with sem:
        try:
            r, w = await asyncio.wait_for(asyncio.open_connection(host, p), 0.4)
            found.append(p); w.close()
        except Exception:
            pass
async def main():
    sem = asyncio.Semaphore(600); found = []
    await asyncio.gather(*(probe(p, sem, found) for p in range(30000, 50000)))
    print(" ".join(map(str, sorted(found))))
asyncio.run(main())
