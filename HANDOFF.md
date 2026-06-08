# SlayTheStats Mod – AI Handoff Document
Generated: 2026-06-08

## Project Overview

**SlayTheStats** is a Slay the Spire 2 mod that tracks gameplay statistics and displays them as hover tips on cards, relics, potions, encounter enemies, event options, and rest site options.

- **Project root:** `C:\Users\arthu\RiderProjects\SlayTheStats\`
- **Framework:** HarmonyLib patches + Godot/STS2 API
- **BaseLib dependency:** `Alchyr.Sts2.BaseLib` v3.1.8 (pinned in csproj; must match DLL in mods folder)
- **Namespace:** `SlayTheStats.SlayTheStatsCode`

---

## File Map

```
SlayTheStatsCode/
├── MainFile.cs                          # Mod entry point, event subscriptions
├── Config/
│   └── SlayTheStatsConfig.cs            # BaseLib SimpleModConfig; ShowStatsHoverTips bool
├── LiveStats/
│   ├── CardDrawPlayData.cs              # { Drawn, Played } – JSON-serializable
│   ├── SupplementaryRunData.cs          # { Ascension, BuildId, CardStats } – JSON file schema
│   ├── InRunStatsTracker.cs             # In-memory accumulator for live run stats
│   └── SupplementaryStatsManager.cs    # File I/O + query layer for .irs files
├── RunData/
│   └── RunDataManager.cs               # Per-(ascension, buildId) stat aggregation from run history
├── Statistics/
│   └── StatisticsManager.cs            # Stub singleton (currently unused)
└── Patches/
    ├── MainMenuReady.cs                 # Triggers LoadAllRuns() + LoadAll() on main menu _Ready
    ├── SaveManagerPatch.cs              # Postfix SaveRunHistory → AddRunToHistory + SaveForRun
    ├── RunLifecyclePatch.cs             # QuitPatch (NGame.Quit → OnQuit); ContinuedRunPatch BROKEN (see below)
    ├── CardDrawPlayPatch.cs             # CombatHistory.CardDrawn/CardPlayStarted → InRunStatsTracker
    ├── CardModelHoverTipsPatch.cs       # CardModel.HoverTips getter postfix
    ├── RelicModelHoverTipsPatch.cs      # RelicModel.HoverTips getter postfix
    ├── PotionModelHoverTipsPatch.cs     # PotionModel.HoverTips getter postfix
    ├── CreatureHoverTipsPatch.cs        # Creature.HoverTips getter postfix (encounter stats)
    ├── EventOptionHoverPatch.cs         # NEventOptionButton.OnFocus prefix+postfix
    └── RestSiteOptionHoverPatch.cs      # RestSiteSynchronizer.LocalOptionHovered postfix
```

---

## Current Bug (OPEN – must fix)

### Symptom
On startup, Harmony throws:
```
HarmonyLib.HarmonyException: Undefined target method for patch method...
  [HarmonyPatch(typeof(RunManager), "SetUpSavedSinglePlayer")]
```

### Location
`RunLifecyclePatch.cs` lines 16–22:
```csharp
[HarmonyPatch(typeof(RunManager), "SetUpSavedSinglePlayer")]
public static class ContinuedRunPatch
{
    public static void Postfix(SerializableRun save)
    {
        SupplementaryStatsManager.OnRunContinued(save.Ascension, RunDataManager.CurrentBuildId);
    }
}
```

### Root Cause
Harmony cannot find `SetUpSavedSinglePlayer` (or `SetUpNewSinglePlayer`) on `RunManager` via either string literal or `nameof()`. These methods exist in decompiled source but are not accessible through the reference assembly at link time. The same failure happened earlier with `SetUpNewSinglePlayer`; that was already fixed by removing the patch and moving the reset logic into `RunManager.RunStarted` in `MainFile.cs`.

### Required Fix
**Remove `ContinuedRunPatch` entirely from `RunLifecyclePatch.cs`.**

Instead, detect a continued run inside the existing `RunStarted` handler in `MainFile.cs`. A continued run is identified by the presence of `current_run.irs` on disk. `RunStarted` fires for both new and continued runs, so:

```csharp
// MainFile.cs – RunStarted handler (replace current content)
RunManager.Instance.RunStarted += state =>
{
    RunDataManager.SetCurrentRun(state.AscensionLevel, currentBuildId);
    InRunStatsTracker.Reset();
    // If current_run.irs exists → this is a continued run; restore partial data.
    // OnRunContinued already calls InRunStatsTracker.Reset() internally, so order matters:
    // Reset first above, then restore overwrites with saved data.
    SupplementaryStatsManager.TryRestoreIfContinued(state.AscensionLevel, currentBuildId);
};
```

Add a new public method to `SupplementaryStatsManager`:
```csharp
/// <summary>
/// Called from RunStarted. If current_run.irs exists, this is a continued run;
/// restore saved partial data. If it doesn't exist, this is a new run and no-op.
/// </summary>
public static void TryRestoreIfContinued(int ascension, string buildId)
{
    var data = TryLoad(InProgressPath);
    if (data == null) return; // new run – nothing to restore
    InRunStatsTracker.RestoreFrom(data.CardStats);
    MainFile.Logger.Info($"[SupplementaryStats] Restored in-progress data ({data.CardStats.Count} cards)");
}
```

Then remove the old `OnRunContinued` method (or keep as dead code — it is currently only called from the broken patch).

After this change, `RunLifecyclePatch.cs` should only contain `QuitPatch`, which works fine:
```csharp
[HarmonyPatch(typeof(NGame), nameof(NGame.Quit))]
public static class QuitPatch
{
    public static void Prefix()
    {
        if (!RunManager.Instance.IsInProgress) return;
        SupplementaryStatsManager.OnQuit(RunDataManager.CurrentAscension, RunDataManager.CurrentBuildId);
    }
}
```

---

## Architecture: Two Data Layers

### Layer 1: RunDataManager (run history files, all stats)
- Loaded at startup from STS2's own save files via `SaveManager.LoadRunHistory()`
- Tracks: card win rates, relic win rates, card lifecycle (floor added, deck size at pick, removal rate, upgrade rate, enchantments), potion pick/use/discard rates, encounter stats (kill rate, avg damage, lethality rank, avg HP on death), event option pick/win rates, rest site choice rates
- Keyed by `(int ascension, string buildId)` — `buildId = ""` is the all-patches aggregate
- Static factory: `RunDataManager.GetInstance(ascension, buildId)`
- Current run accessed via `RunDataManager.CurrentAscension` / `RunDataManager.CurrentBuildId`
- Both specific-patch and all-patches buckets are populated on every run load

### Layer 2: SupplementaryStatsManager (live in-run stats, .irs files)
- Tracks card draw/play counts that are NOT in the run history JSON
- **In-memory:** `InRunStatsTracker` accumulates during a run (patched via `CombatHistory.CardDrawn` / `CombatHistory.CardPlayStarted`)
- **Persistence:** `.irs` files saved alongside game saves at `UserDataPathProvider.GetProfileScopedPath(profileId, "saves/slaythestats")`
  - `current_run.irs` — in-progress run (written on quit, deleted on run completion)
  - `{startTime}.irs` — completed run archives
- Lifecycle:
  - Run ends → `SaveManagerPatch` calls `SupplementaryStatsManager.SaveForRun(history)` → writes `{startTime}.irs`, deletes `current_run.irs`, resets tracker
  - Game quit mid-run → `QuitPatch` calls `SupplementaryStatsManager.OnQuit()` → writes `current_run.irs`
  - Game restart / continue run → **currently broken** (see bug above) → should call `TryRestoreIfContinued()`
  - Main menu ready → `SupplementaryStatsManager.LoadAll()` reads all completed `.irs` files into `_data` dict (skips `current_run.irs`)

### .irs File Format (JSON)
```json
{
  "ascension": 0,
  "build_id": "1.0.0",
  "card_stats": {
    "CARD.STRIKE": { "drawn": 42, "played": 28 },
    "CARD.DEFEND": { "drawn": 37, "played": 30 }
  }
}
```
Key is `card.Id.Entry` (string), value is `CardDrawPlayData { Drawn, Played }`.

---

## Key Patterns

### HoverTip Construction
HoverTip is immutable/struct-like; must use boxing + reflection:
```csharp
var tip = new HoverTip();
object boxed = tip;
AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Title text");
AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed, "Body text");
tip = (HoverTip)boxed;
var list = __result.ToList();
list.Add(tip);
__result = list;
```

### CardVariantKey
Cards are tracked per-variant (base / upgraded / enchanted / upgraded+enchanted):
```csharp
public readonly record struct CardVariantKey(ModelId CardId, int UpgradeLevel, ModelId? EnchantmentId);
```
Upgrade rate and enchantment stats remain at `ModelId` granularity (not per-variant) because `UpgradedCards` in run history only carries `ModelId`.

### Harmony Patch Reliability Notes
- `NGame.Quit` (`nameof`) — **works**
- `CombatHistory.CardDrawn` (`nameof`) — **works**
- `CombatHistory.CardPlayStarted` (`nameof`) — **works**
- `SaveManager.SaveRunHistory` (`nameof`) — **works**
- `NMainMenu._Ready` (`nameof`) — **works**
- `RunManager.SetUpNewSinglePlayer` — **DOES NOT WORK** (removed)
- `RunManager.SetUpSavedSinglePlayer` — **DOES NOT WORK** (must remove)
- Alternative: use `RunManager.Instance.RunStarted` event subscription from `MainFile.Initialize()`

### Config
`SlayTheStatsConfig` extends `BaseLib.Config.SimpleModConfig`. Registered via `ModConfigRegistry.Register(ModId, new SlayTheStatsConfig())` in `MainFile.Initialize()`. Has a single bool `ShowStatsHoverTips` (default true) that gates all hover tip output.

---

## Data Displayed Per Entity Type

| Entity | File | Stats shown |
|--------|------|-------------|
| Card | `CardModelHoverTipsPatch.cs` | Avg floor added, avg deck size at pick, draw count, played count, play rate, win rate, reward pick rate, shop buy rate, removal rate (avg priority), upgrade rate (avg priority), most common enchantment |
| Relic | `RelicModelHoverTipsPatch.cs` | Avg floor acquired, win rate, ancient pick rate, shop buy rate |
| Potion | `PotionModelHoverTipsPatch.cs` | Reward pick rate, shop buy rate, use rate, discard rate |
| Enemy/Encounter | `CreatureHoverTipsPatch.cs` | Kill rate, lethality rank by rate + count (separated: monster/elite/boss), avg HP when dying, avg entry HP, avg turns, avg damage taken, avg gold gained/stolen, avg max HP lost, avg HP healed, avg max HP gained |
| Event option | `EventOptionHoverPatch.cs` | Pick rate, win rate when chosen |
| Rest site option | `RestSiteOptionHoverPatch.cs` | Choice rate (% of campfire visits), avg HP when chosen |

All stats show both current-patch and all-patches values where applicable.

---

## Known Dead Code / Stubs
- `StatisticsManager` (`Statistics/StatisticsManager.cs`) — singleton stub, currently unused
- `OnRunContinued()` in `SupplementaryStatsManager` — will be replaced by `TryRestoreIfContinued()` once the bug fix is applied

---

## Build Notes
- Target framework: .NET (Godot/.NET compatible)
- `pip` not relevant; this is C#
- NuGet: BaseLib pinned to `3.1.8` in `.csproj`
- `using FileAccess = Godot.FileAccess;` required in `SupplementaryStatsManager.cs` to resolve ambiguity between Godot and System.IO
