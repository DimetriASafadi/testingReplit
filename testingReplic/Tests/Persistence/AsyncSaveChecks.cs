using System;
using NewGaza;
using NewGaza.Core;

internal static class AsyncSaveChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var live = GameCatalog.CreateNew(200000);
        new CityDevelopmentService(new EconomyService(live));
        long savedCoins = live.coins;
        GameSaveStore.Enqueue(live);
        live.coins -= 10;
        GameSaveStore.FlushPending();
        var loaded = GameSaveStore.Load(200000, out string warning);
        check(loaded.coins == savedCoins && warning == null, "async writer uses detached snapshot, not live mutations");
        GameSaveStore.Enqueue(live);
        live.coins -= 20;
        GameSaveStore.Enqueue(live);
        GameSaveStore.FlushPending();
        loaded = GameSaveStore.Load(200000, out warning);
        check(loaded.coins == live.coins && warning == null, "lifecycle flush persists latest queued state with valid checksum");
        GameSaveStore.Save(live);
        check(GameSaveStore.Load(200000, out warning).coins == live.coins, "synchronous legacy save serializes after pending writes");
    }
}