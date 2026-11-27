# TFWR game extension points — engineering report

Scope: what a BepInEx/Harmony mod (JavaFarmer) needs in order to (A) let the player switch the programming language and (B) add a "Mods" menu whose toggles ("Cybersecurity", "AI/ML") add unlocks, builtins, items, entities and docs.

File references:
- **C** = .decompiled/Core.decompiled.cs
- **U** = .decompiled/Utils.decompiled.cs
- **A** = .decompiled/Assembly-CSharp.decompiled.cs

Line numbers point at the declaration line. Asset facts come from raw parsing of resources.assets and level0 with UnityPy. The players' assets have no MonoBehaviour typetrees, so I wrote a manual binary reader. It consumed every UnlockSO, ItemSO and FarmObjectSO record exactly (0 bytes left over). Engine: Unity 6000.0.43f1, Mono, BepInEx 5.4.23.x.

Statements marked **(guess)** are not verified in code or assets.

---

## 0. Threading model (read this first)

- `MainSim.Start` (C:4048) starts a background thread, `SimulationLoop`. That thread calls `Simulation.RunNextStep` and then `Execution.Execute(Duration, object lockSimulation)` (C:6242).
- The Unity main thread (`MainSim.Update`, rendering, UI) takes `lockSimulation` for every read of sim state. Public getters such as `MainSim.IsUnlocked` and `GetInventory` also take that lock (C:4404ff).
- `Execution.Execute` has two phases:
  1. Under `lockExecution` only, it runs each drone's `ProgramState.PerformExecutionStep`. **Builtin bindings run here, on the sim thread, without `lockSimulation`.**
  2. Under `lockSimulation`, it calls `Farm.AssertUnlocked` for the collected dependencies. Then it calls `ApplySideEffect(droneId)` (C:6367) for each drone that set `currentSideEffect`.
- Rule for mod builtins: a pure builtin (math, strings, lists) can compute its result directly in the binding. A builtin that reads or changes the farm, items or unlocks must use the **side-effect phase**. This is the same as the vanilla `harvest`, `num_items` and so on.

## 1. Builtin functions

### 1.1 Registration
- `public static class BuiltinFunctions` (C:5134):
  - `private static List<PyFunction> functionList` (C:5136): 47 entries such as `new PyFunction("harvest", Harvest)`. Some use `isFree: true`: get_time, get_tick_count and quick_print. `isFree` is stored but never read anywhere.
  - `private static List<PyFunction> methodList` (C:5187): append, add, remove, pop, insert.
  - Lazily built, cached dictionaries: `private static Dictionary<string,PyFunction> functions, methods`, exposed as `public static Dictionary<string,PyFunction> Functions / Methods` (C:5209). The getter calls `functionList.ToDictionary(f => f.functionName)` once.
  - `GetDroneId` (C:5839), `Send` (C:5860) and `Receive` (C:5879) are implemented but **not registered**. `get_drone_id` is still listed in the megafarm unlock. A mod could register them.
- `public class PyFunction : IPyObject` (C:8056) has these fields:
  - `functionName`
  - `Node syntaxTree`: null for builtins
  - `Scope parentScope`
  - `Func<List<IPyObject>, Simulation, Execution, int, double> binding`
  - `IPyObject methodObject`
  - `bool isFree`

  Ctor used for builtins: `PyFunction(string functionName, Func<...> binding, IPyObject methodObject = null, bool isFree = false)`.
- Where builtins become visible to user code:
  - `Execution.AddProgramState` (C:6224) puts every `Functions.Values.Concat(Methods.Values)` into the global scope: `globalScope.SetVar(name, fn, checkShadow:false, isStatic:true)`.
  - `ImportNode` does the same for each imported module scope (C:~8476).
  - So **adding an entry to the `Functions` dictionary is enough to make a new global builtin**. Read `BuiltinFunctions.Functions` once to force the cache, then `Functions[name] = new PyFunction(name, MyImpl)`. Remove it again to hide it. Do this before an execution starts. Each new `ProgramState` reads the dictionary.
  - Other readers of the dictionary:
    - Parser "not a statement" hint (C:9386)
    - `CodeWindow.GetWordList` completion (C:12866, filtered by `MainSim.Inst.IsUnlocked(name)`)
    - `TooltipUtils.KeywordTooltip` (C:15809)
    - `MarkdownText.GenerateBuiltinsTOC` (C:14364; JavaFarmer already replaces it with `BuiltinsTocPrefix`)
  - The compiler side also needs the function: an `ApiFunctionDef` in src/JavaFarmer.Compiler/GameApi.cs `Defs`, which maps the Java name to the Python name.
- Method-call syntax: `BinaryExprNode` op `"."` (C:7499) evaluates the right-hand side as a name. It copies that `PyFunction` with `methodObject = lhs`. `CallNode` then inserts `methodObject` as parameter 0 (C:7768). As a result, **any** builtin can be called method-style (`x.len()` works), and a new "method" is just a builtin whose first parameter is the receiver. A method only needs to be in `Methods` for completion after a dot (`GetSubWordList`, C:12890).

### 1.2 Call path, argument checking, return values, errors
- `CallNode.Execute` (C:7746ff) does the following for a builtin (`syntaxTree == null`):
  1. `state.currentDependencies.Add((func.functionName, wordStart, wordEnd))` (C:7790). This is the unlock gate (§1.4).
  2. If `binding == null`, it throws `error_name_not_defined`.
  3. `double ops = func.binding(parameters, execution.sim, execution, state.DroneId)`.
  4. `CheckIncrementOpCount(state, execution, ops)` (C:8688) adds the ops to `state.OpCount`. It yields if the target op count is reached **or if `currentSideEffect != None`**.
- Arguments: `List<IPyObject> parameters`, already evaluated.
- Helpers, both private, so a mod copies them:
  - `CorrectParams(List<IPyObject>, List<Type>, string fn)` (C:5250) throws `ExecuteException(LocalizeAndFormat("error_wrong_number_args", fn+"()", n))` or `"error_wrong_args"`.
  - `NoParams(parameters, fn)` throws `"error_expected_no_args"`.
- Return value: a pure builtin sets `exec.States[droneId].ReturnValue = <IPyObject>` and returns its tick cost. If it never sets `ReturnValue`, the caller sees the previous value, so always set `PyNone`.
- Errors: `throw new ExecuteException(string messageOrLocKey, int startIndex=-1, int endIndex=-1)` (U:536). The message is passed through `Localizer.Localize` when shown (C:~6735). An exception thrown in the binding propagates through the node enumerator. ProgramState turns it into `SideEffect.Error`: the error is shown in the code window, `sim.Error()` runs and the "CAUSE_A_RUNTIME_ERROR" achievement fires (C:6727ff).
- Side-effect builtins follow the vanilla pattern, for example `NumItems` (C:~5560): in the binding, set `programState.currentSideEffect = SideEffect.X` plus `currentSideEffectArgument` (IPyObject) and `currentSideEffectArgument2` (object), and return 0. `ApplySideEffect` then:
  - sets `ReturnValue`
  - computes `num` (the tick cost)
  - does `programState.OpCount += num` and returns `num`

  Afterwards `Execute` resets the side-effect fields (C:~6330).
- **Adding a side effect from a mod.** `SideEffect` is an enum (C:6815, values 0..38). Use out-of-range values, e.g. `(SideEffect)1000 + k`. Add a Harmony **prefix on `Execution.ApplySideEffect(int)`** (private; JavaFarmer already patches it). For mod values the prefix:
  1. does the work
  2. sets `ReturnValue`
  3. does `programState.OpCount += ops`
  4. sets `__result = ops` and returns `false`

  The vanilla switch has no default case, so an unknown value would fall through with cost 0 and no return value. Catch your own exceptions and rethrow them as `ExecuteException`. Vanilla catches `ExecuteException` around `ApplySideEffect` and converts it to an Error (C:~6318).

### 1.3 Value representation (U = Utils)
- Every value implements `IPyObject` (U:10306): `IPyObject DeepCopy(Dictionary<object,object>)` plus a default method `int Size() => 1`. `Size()` drives the tick cost of set, dict and send operations.
- Value types:
  - `PyNumber(double)` (U:10262): every number is a double. It converts implicitly to `double`.
  - `PyBool(bool) : PyNumber` (U:9638)
  - `PyNone`: `PyNone.Instance` (U:10243)
  - `PyString(string)`, field `str` (U:10645). It is an `IPySequence`, so it can be indexed, sliced, iterated and has a length.
  - `PyList(List<IPyObject>)`, field `list`; implements `IList<IPyObject>` (U:10044)
  - `PyTuple(List<IPyObject>)` (U:10712)
  - `PySet(HashSet<IPyObject>)`, field `set` (U:10577)
  - `PyDict(Dictionary<IPyObject, PyObjectBox>)`, field `dict`; values are boxed: `new PyObjectBox(obj)`, `.obj` (U:9721)
  - `PyRange` (U:10411), `PyModule` (U:10183)
  - `PyConstBag` (U:9654): the `Items`, `Entities` and similar namespaces
  - `PyGridDirection` (U:9915), `PyDroneHandle` (U:9872)
  - `InternalPySequence` (U:9600): the argument tuple; its `.elements` field is the parameter list
- The ScriptableObjects `ItemSO`, `FarmObjectSO`, `UnlockSO`, `HatSO` and `LeaderboardSO` implement `IPyObject` themselves (`DeepCopy` returns `this`). `Items.Hay` *is* the ItemSO.
- A mod can define new value classes that implement `IPyObject`, e.g. a `PyPacket`. Requirements:
  - Override `Equals`/`GetHashCode` if the value may be used as a dict key or set member.
  - Make `ToString` meaningful. Vanilla `CodeUtilities.ToNiceString` (U:279) falls back to it; JavaFarmer replaces it with `ValueFormatter.Format`.
- `BuiltinFunctions.ItemsToNewDict(ItemBlock)` is public. It converts an `ItemBlock` into `{Items.X: n}`.

### 1.4 Unlock gating of builtins
- `currentDependencies` → `Farm.AssertUnlocked(dependency, ws, we)` (C:3077) → `IsUnlocked(s)` = `NumUnlocked(s.ToLower()) > 0` (C:3072).
- If the name is not unlocked, the game throws `error_missing_x_unlock`, naming the first `UnlockSO` whose `unlocks` list contains the name, or `error_missing_unlock`.
- `Farm.unlocks` (`Dictionary<string,int>`, C:2901) holds **every** granted name: unlock names, builtin names, keywords, items, entities and hats. Names enter it in two ways:
  - `Farm.startUnlocks` (C:2899): grass, soil, harvest, pass, do_a_flip, pet_the_piggy, grassland, hay, straw_hat.
  - `UnlockAllIn(UnlockSO)` (C:3160), which grants every string in `unlockSO.unlocks`. A leading digit means "granted at level N", e.g. `"2for"` in `expand`.
- **To gate a new builtin**, put its name in the `unlocks` list of the mod's UnlockSO. Its completion, tooltip and TOC entry then appear only once it is unlocked.
- Node types also add keyword dependencies: "while"/"if" (C:7682), "for" (C:8253), "lists" (C:8577), "dicts" (C:8161), "sets" (C:8954), "functions" (C:8040), "import" (C:8528), "operators", "variables". JavaFarmer already intercepts these through `CheckDependencies` prefixes and `DependencyPolicies`.

### 1.5 Tick and time cost model
- `Execution.ACTION_OPS = 200` and `OPERATION_OPS = 1` (C:6174). `Node.TICKS_PER_OP = 1`.
- `Simulation.OpDuration` = 0.0025 s at speed factor 1 (C:4816). `ChangeExecutionSpeed` sets it to `2.5 ms / speedFactor`.
- `Farm.MaxSpeedFactor() = 1.5^speedUpgrades`, ×2 while the player has `power`.
- So 1 tick = 2.5 ms at base speed, and an action costs 200 ticks = 0.5 s.
- Vanilla costs:

  | Kind | Cost (ticks) |
  |---|---|
  | Successful actions (harvest, plant, move, till, swap, use_item, unlock, change_hat, set_*) | 200 |
  | Failed actions and queries (can_*, get_*, num_items, measure, get_cost, …) | 1 |
  | print, do_a_flip, pet_the_piggy | `floor(1/OpDuration.Seconds)`, i.e. 1 real second |
  | list(x) | 1 + len |
  | dict(x) | 1 + 2·len |
  | min/max | number of comparisons |
  | send | `max(1, Size())` |

- Each side effect adds `UsedPower += ops/200/30` (C:~6338). `Farm.UsePower` drains the `power` item every 0.2 s.
- `Execute` runs at most `min(199, targetTime/OpDuration)` ops per slice. Drones are interleaved by `OpCount`.
- A mod builtin chooses its cost freely through the returned double, or through `num` in its side-effect handler.
- Periodic farm mechanics: `Simulation.StartTimer(Action func, Duration time)` (C:4891). The pattern is `Farm.ReceiveWater`, which re-arms itself (C:~3245). The `Farm` ctor registers its timers (C:2955–2957). A mod can postfix the `Farm` ctor (C:2920) and start its own timers, such as passive "data" income or an "intrusion" event. Timers run on the sim thread under `lockSimulation`.

### 1.6 Docs and tooltip for a new builtin
- Hover tooltip: `TooltipUtils.KeywordTooltip(word)` (C:15809). It shows `Localizer.Localize("code_tooltip_" + word)`, only if the word is unlocked and the key exists. The docs link is `"functions/" + word`.
- `DocsWindow.LoadDoc("functions/x")` (C:13061) renders `code_tooltip_x` plus "unlocked in …". The "unlocked in" link comes from `TooltipUtils.GetKeywordDocsPage` (C:~15790). That function caches in a static `keywordDocs` dictionary (`private static Dictionary<string,string> keywordDocs`), built once from all UnlockSOs. **Reset it to null** with reflection after the mod adds or removes unlocks.
- Strings file format (`Localizer.LoadLang`, U:9487): `@key = value`, multi-line, entries separated by `\n@`. Example from EN/Strings/code_tooltips.txt: `@code_tooltip_harvest = `harvest()`\n…\ntakes `200` ticks…`.
- JavaFarmer's `MergeStrings` (Patches.cs, `LoadLangPostfix`) already injects `content/Strings/*.txt` keys into `Localizer.language`. **New keys work too.** So a mod builtin only needs `@code_tooltip_<name>` in a content strings file.

## 2. Unlocks / tech tree

### 2.1 Data model
- `UnlockSO : ScriptableObject, IPyObject` (C:2326). Fields:
  - `unlockName` (lowercase)
  - `description` (loc key)
  - `docs` (path, e.g. "docs/unlocks/x.md")
  - `Mesh mesh`
  - `displayCode`
  - `List<string> unlocks`
  - `parentUnlock`
  - `unlockedHat`
  - `int order`
  - `ItemBlock unlockCost`
  - `List<ItemBlock> multiUnlockCost`
  - `float multiUnlockFactor = 2`
  - `multiUnlockDescr`
  - `MultiUnlockDescrMode multiUnlockDescrMode`: None / AdditivePercent / GridSize / Megafarm / Per10Seconds
  - `additivePercentStart`, `additivePercentFactor = 2`
  - `int maxUnlockLevel = 1`
  - `bool enabled = true`
  - `IsMultiUnlock => maxUnlockLevel > 1`
  - `ToString()` returns `"Unlocks." + ToUpperSnake(unlockName)`.
- Loading: `ResourceManager.LoadAll()` (C:2194) calls `Resources.LoadAll<UnlockSO>("Unlocks/")` and then `unlocks[unlockName] = so` into `private static Dictionary<string,UnlockSO> unlocks`. It deserializes `unlockCost` and `multiUnlockCost` before that. `LoadAll` returns early if any of its static collections is already non-null, so it runs **once per process**, not once per scene load. It is called from `Menu.Start` (C:14434), right before `Achievements.LoadStats()` and `Saver.Load(MainSim.Inst)`.
- Actual tree (34 UnlockSOs, parsed from resources.assets):
  - **The only root is `loops`.**
  - Children by parent, in order:

    | Parent | Children |
    |---|---|
    | loops | hats, speed, grass |
    | speed | expand, plant |
    | plant | carrots, debug, operators |
    | operators | senses, variables |
    | variables | lists, functions |
    | lists | dictionaries |
    | dictionaries | costs |
    | costs | auto_unlock |
    | functions | import, utilities |
    | debug | debug_2, timing |
    | timing | simulation |
    | simulation | leaderboard |
    | carrots | watering, trees |
    | watering | fertilizer, sunflowers |
    | fertilizer | mazes |
    | mazes | top_hat, megafarm |
    | trees | pumpkins |
    | pumpkins | cactus, polyculture |
    | cactus | dinosaurs |
    | dinosaurs | the_farmers_remains |

  - Examples of the `unlocks` lists:
    - `senses` grants get_entity_type, get_ground_type, Grounds, get_pos_x, get_pos_y, None, num_items, Items, num_unlocked.
    - `utilities` grants min, max, abs, random.
    - `expand` grants move, North…West, 2for, 2range, 2get_world_size.
  - Costs are `{item: n}` maps (e.g. senses = {hay:100}). Multi-level unlocks carry `multiUnlockCost` lists. Past the end of that list, the cost is `last * multiUnlockFactor^(level-count)` (`Farm.GetUnlockCost`, C:~3010).

### 2.2 Tree UI
- `ResearchMenu : MonoBehaviour` (C:14870), on `OverlayUI/ResearchCanvas/ResearchMenu`.
- `Setup()` (C:15022):
  1. Destroys the container's children.
  2. Instantiates the `researchPrefab` (UnlockBox) once for each **enabled** UnlockSO.
  3. Links children via `parentUnlock`.
  4. Lays out **only `rootUnlocks[0]`** with `GetLayout`/`SetUIPositions` (C:15078). This is a recursive tidy tree; children are ordered by `order`, and the spacing comes from serialized `horizontalSpacing`/`verticalSpacing`.
  5. Draws connector lines.

  ⇒ **A mod unlock must have an existing `parentUnlock`.** A second root would be instantiated but never positioned. Put an extension's root node (e.g. `cybersecurity`) under an existing node such as `senses`, `functions` or `loops`, and chain the extension's own nodes under it.
- `Setup()` is called only from `Saver.Load` (C:~11091). That is once per scene load. **It can be called again at runtime to rebuild the tree after toggling.**
- `ResearchMenu.Update` calls `SetupRec` on every root each frame. It also fires the "UNLOCK_EVERYTHING" achievement when every box is maxed (see §7).
- `UnlockBox.SetupRec(bool unlockable, HashSet<string> openedUnlockDocs, ItemBlock inventory, Dictionary<string,int> unlocks, out bool allUnlocked)` (C:15962):
  - State comes from the unlocks dictionary.
  - The first time level 1 is reached, it opens `unlockSO.docs` with `workspace.OpenAndGoTo`.
  - Visual: if `mesh != null`, it shows `Resources.Load<Sprite>("UnlockTextures/" + unlockName)`. Otherwise it shows `CodeUtilities.SyntaxColor2(unlockSO.displayCode)` as text.
  - **Mod unlocks: set `mesh = null` and give a `displayCode` text.** That is the cheapest path. JavaFarmer's `UnlockBoxPrefix` already swaps in `displayCode` from content/unlock_display_code.txt. To use an icon instead, set a non-null mesh (any existing one) and postfix `SetupRec` to set the private `image.sprite` from a mod Texture2D.
- `UnlockBox.ButtonClicked` (C:16040) calls `MainSim.Inst.UnlockOrUpgrade(unlockSO)` → `Farm.UnlockOrUpgrade` (C:3100). That checks the parent, the max level, the singleDrone rules and the cost. It then calls `Unlock(name, level+1)` and `UnlockAllIn`. Afterwards `unlockedHat` is granted.
- Tooltip: `TooltipUtils.UnlockTooltip(name)` (C:15710). It uses the header `Localizer.Localize(unlock.unlockName)` (so add a loc key named after the unlock), `Localize(description)`, and the `multiUnlockDescr`/mode text. The `unlock_tooltip_template_cost` string contains a `{{itemblock cost unlock <name>}}` custom section that renders the live cost (`MarkdownText.Setup`, C:14051).
- Docs TOC: `MarkdownText.GenerateUnlockTOC` (C:14340) lists enabled unlocks by **`unlock.name`** (the asset name, not `unlockName`). It links to `unlock.docs`, or `"unlocks/<unlockName>"` when empty. ⇒ Set `so.name = unlockName`.

### 2.3 Adding unlock nodes at runtime (recommended)
1. Create the nodes with `ScriptableObject.CreateInstance<UnlockSO>()`. Set:
   - `name` = `unlockName` (lowercase, no trailing `_<digits>`, see §5.3)
   - `description`, `docs`
   - `displayCode`, `mesh = null`
   - `unlocks`: new List, holding the builtin names, keywords, items and entities to grant
   - `parentUnlock` (existing), `order`
   - `unlockCost`: build it with `ItemBlock.CreateEmpty()` + `AddItem(StringIds.GetItemId("hay"), 100)`, *after* item ids exist
   - `multiUnlockCost`: a new List; must be non-null
   - `maxUnlockLevel`, `enabled`
2. Insert them into the private static `ResourceManager.unlocks` dictionary (`AccessTools.Field(typeof(ResourceManager), "unlocks")`). Do it in a **postfix on `ResourceManager.LoadAll`**, guarded so it runs once (`LoadAll` is a no-op on later calls).
3. Everything else picks the nodes up automatically:
   - `ResearchMenu.Setup` (the tree)
   - `Scope.EvaluateConstant("Unlocks")` (C:10490): the `Unlocks.X` constants, filtered by `enabled` and rebuilt on every evaluation
   - `get_cost`, `unlock()`, `num_unlocked`
   - completion (`GetSubWordList "Unlocks"`)
   - the docs TOC
   - `AssertUnlocked` error messages
4. To toggle at runtime without a reload:
   1. Add or remove the SOs, or flip `enabled`.
   2. Null `TooltipUtils.keywordDocs`.
   3. Null `Farm.unlockedIn` on `sim.farm` (private; the per-farm cache in `GetUnlockOf`).
   4. Call `MainSim.Inst.researchMenu.Setup()`.

   This must not run while an execution runs. A scene reload is simpler (§4.4).
5. Pitfall — leaderboards: `leaderboard_run` with `everythingUnlocked` (C:5661) uses `GetUnlockStrings(ResourceManager.GetAllUnlocks())`, so mod unlocks would be included in such runs. JavaFarmer blocks uploads anyway (§7). `simulate()` takes the player's own unlock list.
6. Upgrade levels: set `maxUnlockLevel > 1` and `multiUnlockCost = [cost1, cost2, …]`. The level is read with `sim.farm.NumUnlocked("name")`. Level-gated grants use digit-prefixed entries in `unlocks` (`"2my_builtin"`).

## 3. Items, entities, grounds, hats

### 3.1 Items
- `ItemSO : ScriptableObject, IPyObject` (U:2092). Fields: `itemName`, `description` (loc key), `docs`, `Mesh mesh`, `trackStats`, `enabled = true`, `float priority`, `[NonSerialized] int itemId`.
- The 12 vanilla items and their priorities: hay 1, wood 2, carrot 3, pumpkin 7, cactus 8, bone 9, weird_substance 9.5, gold 10, water 11, fertilizer 12, power 13, piggy 100000.
- Loading in `LoadAll`, in this order:
  1. `items = Resources.LoadAll<ItemSO>("Items/").OrderBy(priority).ToArray()`
  2. `itemId = index`
  3. `StringIds.SetItemIds(items.Select(x => x.itemName))` (U:11126; called only here, C:2207)
- `ItemBlock` (U:1822) is `[NonSerialized] double[] items`, sized by `StringIds.NumItems` at creation, plus `[SerializeField] SerializableItem[] serializeList` (`{name, nr}`) for JSON. Every ItemBlock (inventory, costs, yields) is a fixed-length array indexed by `itemId`. `Add`, `Contains` and `Remove` loop over `items.Length` and index the other block, so **mixing blocks of different lengths throws IndexOutOfRange**.
- ⇒ **New items must exist before `StringIds.SetItemIds` runs, and the number of items must never change for the rest of the process.**
- Recommended hook: **Harmony prefix on `StringIds.SetItemIds(ref IEnumerable<string> items)`**. At that moment `ResourceManager.items` is set but no ItemBlock has been deserialized yet. In the prefix:
  1. Create the mod `ItemSO`s. Set `name = itemName`, because `ItemUI` calls `TooltipUtils.ItemTooltip(item.name)`. Set `trackStats = false`.
  2. Append them to the array and re-sort by priority. Use priorities below 100000 if they should show before piggy.
  3. Reassign `itemId`.
  4. Write the array back to the private static `ResourceManager.items`.
  5. Replace the `items` argument with the new name list.

  The rest of `LoadAll` (FarmObjectSO.cost, LeaderboardSO and UnlockSO deserialization) then uses the new `NumItems`.
- **Always register the extension items when the plugin loads, even if the extension is toggled off.** Hide them with `enabled = false`:
  - `Items` const bag (C:10497) and the items TOC filter on `enabled`.
  - Completion requires `IsUnlocked(itemName)` and `enabled`.
  - The inventory UI (`Inventory.Update`, C:13408) shows every item with count > 0, whatever `enabled` says.
- Icons: `ItemUI.Setup(int itemId, double c)` (C:13535) does `image.sprite = Resources.Load<Sprite>("ItemTextures/" + item.itemName)`. That is null for mod items, so add a postfix that creates the sprite with `Sprite.Create(tex, rect, pivot)`. Load the texture from a PNG in content/ (needs the ImageConversion module, `Texture2D.LoadImage`), or reuse an existing `ItemTextures/<vanilla>` sprite. In-docs images use `![](Name<height>)` → `ResourceManager.GetSprite(name)` = `Resources.Load<Sprite>("Sprites/"+name)`. A mod would postfix `ResourceManager.GetSprite` to serve its own.
- Item effects are hardcoded by name in `ApplySideEffect` `UseItem` (water, fertilizer, weird_substance, C:~6455). A mod item that should work with `use_item` needs the ApplySideEffect prefix. A dedicated mod builtin is usually simpler.
- Harvest flow:
  - `Drone.Harvest()` (C:2658) calls `farm.CollectItems(entity.Harvest(out dur), pos, dur)`.
  - `Growable.Harvest` → `GetYield()` (C:~1141): `dropAmount * YieldFactor` of `objectSO.dropItemId`.
  - `FarmObject.YieldFactor` = `2^(NumUnlocked(yieldUpgradeName)-1) * harvestFactor`; `Growable` multiplies it by the companion bonus.
  - `Farm.CollectItems` (C:2965) adds the items, then calls `Achievements.CollectItem` for `trackStats` items and plays the effect.
  - Mod code can call `sim.farm.CollectItem(itemId, n, localPos)` directly from a side-effect handler or a timer.

### 3.2 Entities and grounds
- `FarmObjectSO : ScriptableObject, IPyObject` (C:915). Fields:
  - `className`: the C# type name
  - `isGround`
  - `objectName`, `description`, `docs`
  - `Color color`: the hit-VFX colour
  - `List<Mesh> meshes`, `List<Mesh> gooMeshes`
  - `canBeSwapped`
  - `List<string> placeableOn`
  - `harvestSound`
  - `verticalGrowth`, `meanGrowTime`, `growTimeDeviationPercent`
  - `canHaveCompanion`, `randomRotation`, `canBePlanted`, `canBeOverplanted`
  - `dropItem` / `[NonSerialized] dropItemId`, `double dropAmount`
  - `yieldUpgradeName`
  - `ItemBlock cost`: the seed cost, doubled per yield-upgrade level
- Vanilla objects (className): apple (Apple), bush (BushPlant), cactus (Cactus), carrot (Growable), dead_pumpkin (DeadPumpkin), dinosaur (Dinosaur), grass (Growable), hedge (HedgePlant), pumpkin (Pumpkin), sunflower (Sunflower), treasure (Treasure), tree (TreePlant); grounds: grassland (Ground), soil (Soil).
- Loading: `Resources.LoadAll<FarmObjectSO>("FarmObjects/")` → `farmObjects[objectName]`, after `cost.Deserialize()` and `dropItemId = StringIds.GetItemId(dropItem)` (C:2209ff). Mod FarmObjectSOs go into the private static `ResourceManager.farmObjects` in the same `LoadAll` postfix. Remember to set `dropItemId` yourself.
- Grid: `GridManager` (C:3488) has `Dictionary<Vector2Int, FarmObject> entities` and `grounds`, plus `double[,] waterVolume`. These are runtime class instances, not an enum.
- Creating an object: `SetEntity(pos, name)` (C:3726) → `SetFarmObject` → `CreateFarmObject(so)` (C:3823) does `Activator.CreateInstance(farmObjectTypes[so.className])`. **`farmObjectTypes` is built only from `typeof(FarmObject).Assembly`, i.e. Core.** A mod subclass in the plugin assembly would throw `KeyNotFoundException`. Fix: prefix `GridManager.CreateFarmObject`, or pre-fill the private static `GridManager.farmObjectTypes` with the mod types. `Hat.CreateHat` (C:2098) has the same issue with its `hatTypes`.
- Planting: `plant(Entities.X)` → `Drone.Plant(so, state)` (C:2709). It requires `canBePlanted`, the ground in `placeableOn` and `AssertUnlocked(objectName)`. It removes `cost * 2^(yieldLevel-1)`, calls `SetEntity` and `OnPlanted`. `Entities.X` comes from `EvaluateConstant` (all `!isGround` FarmObjectSOs, **not** filtered by enabled, C:10500). A disabled-extension entity therefore still resolves, but `plant` fails on `AssertUnlocked`. Remove the SOs if they must vanish completely.
- Virtual hooks on `FarmObject` (C:710): `GetMeshes`, `GetYield`, `Harvest`, `OnPlanted`, `UpdateFarmObject`, `Measure()` (what `measure()` returns), `CanDroneMoveToFrom`/`AwayTo`, `ToSerializeString`/`SetValues`, `OnRestart`, `OnFree`. `Growable` (C:999) adds growth timers, watering, companions and "weird" goo.
- **Rendering and model reuse:**
  - `FarmRenderer.Update` (C:3318) draws every farm mesh with one shared `material` via `Graphics.DrawMeshInstanced`.
  - Meshes have channels pos, normal, tangent and UV0, **no vertex colours**. Colour comes from the "ColorPalette" material texture (_BaseMap) through UVs.
  - All 129 meshes in resources.assets have `m_IsReadable = false`, so a mod **cannot read or retint vanilla meshes on the CPU** (mesh.uv is inaccessible in a player build).
- Cheapest routes for a new entity:
  - (a) Reuse an existing mesh list unchanged, e.g. a "data crop" that looks like a sunflower. Copy `gooMeshes` too, because `Growable.ToggleWeird` does `Next(gooMeshes.Count-1)`.
  - (b) A postfix on `FarmRenderer.Update` that draws mod entities with a cloned material and a different `_BaseColor`/`_BaseMap` offset **(guess: shader properties not verified)**. `GetMeshes` would then return nothing for them.
  - (c) Procedural `new Mesh()` instances (readable) with UVs into the palette texture **(guess: palette layout unknown)**.
- New grounds are possible: an `isGround` SO with className "Ground"/"Soil" and a mod builtin calling `drone.ChangeGround(name)`. `till` hardcodes "soil" (C:~6420).
- Feasibility summary:
  - New items, unlocks and builtins: **easy and robust**.
  - New entities that reuse a vanilla `className` and meshes: **moderate**.
  - Custom FarmObject subclasses: moderate (type-dictionary patch).
  - Distinct 3D visuals: **hard**.
  - Cheapest design: model extensions as items, builtins and unlocks plus simple timers. Add at most 1–2 entities that reuse vanilla meshes.

### 3.3 Hats
- `HatSO` (C:2114): `className` (Hat/DinosaurHat), `hatName`, `hatMesh`, `isGolden`, `hidden`, …; 29 assets.
- Loaded from "Hats/". `Hats` const bag; unlocked by name through `Farm.UnlockHat`.
- Not needed for this plan.

## 4. Menus / UI

### 4.1 Scene structure (level0)
- `OverlayUI/MenuCanvas/Menu` [`Menu`, C:14414]. Serialized fields: `menu`, `uiCanvases[]`, `titlePage`, `saveChooser`, `options`, `saveButton`.
  - `TitlePage` has no layout group. Its buttons are positioned by hand: anchor (0,0.5), pivot (0,0.5), size 200×40, x = 125.35. Each has [LayoutElement, ColoredButton, Button, Image] and child `TextTitle` [TextMeshProUGUI, TMPTextLocalizer]. Their y values:

    | Button | y |
    |---|---|
    | PlayButton | 156.9 |
    | LoadButton | 94.2 |
    | SaveButton | 31.5 |
    | OptionsButton | -31.2 |
    | DiscordButton | -93.8 |
    | QuitButton | -156.5 |

    The step is about 62.7.
  - The onClick handlers are persistent inspector calls to the `Menu` methods `Play`, `ChooseSave`, `PressSaveButton`, `Options`, `JoinDiscord` and `Quit` (C:14490–14577). The mapping by name is a **(guess)**: onClick typetrees are not readable without a generator.
  - `Options` [`OptionMenu`, C:14578] contains `Scroll View/TabBar` (HorizontalLayoutGroup; BackButton plus the instantiated category tab buttons) and `Scroll View/Viewport/Content` (VerticalLayoutGroup; option rows).
  - `SaveChooser` [`SaveChooser`, C:14653].
- The same `Menu` is the pause menu. `InputBoss.Update` (C:4984) opens it with the "menu" key (Esc) via `MainSim.Inst.menu.Open()` and closes it with `Play()`. `Menu.Open()` hides the workspace and `uiCanvases`, shows `titlePage`, and hides `saveChooser`/`options`.
- Top-right in-game buttons: `OverlayUI/ResearchCanvas/Buttons/{AddFileButton, InfoButton, ResearchButton, WarningIcon}`.
  - Each is 60×60, anchored top-right (1,1), pivot (1,1), at y = 473. Their x values: AddFile 751, Info 828, Research 905; WarningIcon is at 645, y 442, pivot 0.5. Spacing is 77 px.
  - `ResearchMenu` holds references `plusButton`, `infoButton`, `openCloseButton` and toggles them in `OpenCloseMenu` (C:15129).
  - The handlers are presumably `Workspace.AddNewWindow` (C:16732: new file "f<N>"), `Workspace.CallAddNewDocsWindow` and `ResearchMenu.OpenCloseMenu` **(guess on the exact wiring)**.

### 4.2 Adding a "Mods" button
- Postfix `Menu.Start` (C:14434). It runs on every scene load, because `SceneManager.LoadScene(0)` reloads everything.
- Then:
  1. Find `titlePage.transform.Find("OptionsButton")`.
  2. `Object.Instantiate(btn, titlePage)`, rename it "ModsButton" and set its anchoredPosition.
  3. Either shift Discord and Quit down by 62.7, or place the new button at y = -219.
  4. Set the label: `GetComponent<ColoredButton>().Text = "mods_button"`. `TMPTextLocalizer` re-localizes on text change, and a missing key shows as-is.
- **Clear the cloned persistent listener:** `RemoveAllListeners()` does not remove inspector calls. Assign `GetComponent<Button>().onClick = new Button.ButtonClickedEvent()`, then `AddListener(OpenModsPanel)`. `ColoredButton.OnClick` returns `button.onClick`, and Instantiate remaps that private reference to the clone.
- The same cloning works for a top-right in-game button. Clone `InfoButton` under `ResearchCanvas/Buttons` at x = 674 or x = 982, and swap `Image.sprite` on its child `Image`.

### 4.3 Options and settings storage
- `OptionHolder` (U:11646) is a static `Dictionary<string, object> options`.
  - It is persisted to `Application.persistentDataPath + "/options.txt"` as `key = value` lines, rewritten on every `SetOption` (U:11693).
  - It does not use PlayerPrefs.
  - `LoadOptions` (U:11745) seeds defaults from `Resources.LoadAll<OptionSO>("Options/")`, then reads the file.
  - `ValueOfString` turns numbers into float and parses key combos.
  - `OnOptionChanged(string)` is an event.
  - `activeSave` (the current save name) is also stored here.
- `OptionSO` (U:11936) fields: `OptionUI optionUI` (prefab), `optionName`, `tooltip` (loc key), `defaultValue`, `category = "gameplay"`, `importance`. `CycleOptionSO : OptionSO` adds `List<string> options` (U:11523). `CycleOptionUI.Clicked` cycles through them (U:11551).
- Assets: 13 Cycle, 8 Slider and 17 KeyBind options. Categories: general, visuals, audio, controls, errors. All CycleOptionSOs share one `optionUI` prefab (pathID 1958). The vanilla `language` option (EN…PL) is the **UI** language; do not overload it.
- `OptionMenu.Setup()` (C:14597) does nothing once `categoryButtons` is set (the first open). It reads `ResourceManager.GetAllOptions()` (C:2304), which calls `Resources.LoadAll` each time. It orders by importance descending and creates one tab per distinct category. `ChangeCategory(string)` (C:14627, private) instantiates `so.optionUI` and calls `Setup(so)`. The row title is the **raw `optionName`** (not localized); the tooltip is localized.
- **Code language option (A), cheapest:**
  1. Create a `CycleOptionSO` instance with `optionName "code language"`, options [Python, Java, C, C++, C#, Rust, Zig, JS, R], `optionUI` copied from any vanilla CycleOptionSO, `category "general"` (or "mods") and a tooltip key.
  2. Postfix `ResourceManager.GetAllOptions` to append it.
  3. Seed the default: postfix `OptionHolder.LoadOptions` and write the private `options` dictionary, or call `SetOption` once if `GetOption` is null. Without a default, `CycleOptionUI.UpdateValue` shows "".
  4. React through `OptionHolder.OnOptionChanged`.

  A per-window alternative is a small ColoredButton cloned into each CodeWindow header (`CodeWindow` has `fileNameText`, `executeButton` and `stepByStepButton`, C:11971ff), set in a postfix on `Workspace.OpenCodeWindow` (C:16575). It would store the language per file (§5.2).
- **Mods panel (B):**
  - Option 1 (cheapest): category "mods" with CycleOptionSOs "Cybersecurity" and "AI/ML" (enabled/disabled). The title-page "Mods" button calls `menu.Options()` and then the private `ChangeCategory("mods")` through reflection. The other tabs stay visible.
  - Option 2 (dedicated panel): `Instantiate(menuOptionsGO, menuGO)`. Prefix `OptionMenu.Setup`; for the clone, build only the mod rows. `ChangeCategory` and `allOptions` are private, so either set `allOptions` by reflection or replicate the 10 lines. Postfix `Menu.Open`/`Play` to hide the clone. The cloned BackButton keeps its inspector call. That is presumably `Menu.Open`, which shows the title page, but the clone stays visible until hidden by the postfix.
  - Use `MainSim.Inst.warningPopup.ShowPopup(text, List<WarningPopup.ButtonData>)` (U:12285; `ButtonData(string text, UnityAction cb)`) for an "Apply now (reloads save)?" prompt.

### 4.4 Applying toggles
- Recommended: save and reload, the way switching saves does (`SaveChooser.LoadSave`, C:~14720): `Saver.StopFileWatcher(); Saver.Save(MainSim.Inst); SceneManager.LoadScene(0);`.
- Before the reload, the mod changes its own static registrations:
  - add or remove the UnlockSOs and FarmObjectSOs in the ResourceManager dictionaries
  - add or remove the builtins in the `BuiltinFunctions.functions` dictionary
  - set `ItemSO.enabled`
  - null `TooltipUtils.keywordDocs`
- `LoadAll` will not run again, so these changes persist across the reload. After the reload, `Saver.Load` → `SetupSim` → `researchMenu.Setup()` rebuilds everything, and `Menu.Start` ends in `Open()` (the title page).
- Do not change the item count (§3.1).

## 5. Save format

### 5.1 Layout and content
- Root: `Application.persistentDataPath + "/Saves/<name>"`. The name comes from `OptionHolder "activeSave"` (default "Save0").
- `GetPathOfSaveFile` (C:11290) prefers `Saves/<name>/save.json` and falls back to the legacy `Saves/<name>.json`. The user's captured save uses the legacy `Save0.json` together with the `Save0/` folder.
- Backups go to `persistentDataPath/Backup/<name>_backup<N>/` (`CreateBackupPath`, with thinning).
- `SaveGame` (C:11331; `JsonUtility`) fields:
  - `ItemBlock items` (`{"serializeList":[{"name":"hay","nr":4572.0},…]}`)
  - `dockedFiles`, `minimizedFiles`
  - `openFilePositions`, `openFileScrollPositions`, `openFileSizes`
  - `openDocPages` (`[{key:"docs0", value:"docs/home.md"}]`)
  - `List<string> unlocks`
  - `int version = 3`
- **The farm grid is not saved** (v3): `Saver.Load` passes null grounds and entities. It also does not store code-window language, options or drones.
- `unlocks` comes from `Farm.SerializeUnlocks()` (C:3038): every key of `Farm.unlocks` as `name` or `name_<level>` when the level is above 1, e.g. `"grass_2","soil",…,"speed_2","expand_3","north",…`. Granted builtin and keyword names are stored explicitly.
- Load (`Farm` ctor, C:2920):
  - For each string: if it has a `_<int>` suffix and does not start with "debug", it is split into a name and a level.
  - `Unlock(name, level)`. If `ResourceManager.GetUnlock(name) != null`, also `UnlockAllIn`.
- **Unknown unlock names do not break loading.** They are simply kept in `Farm.unlocks` and written back on the next save. So mod unlock and builtin names survive an "extension off" session and come back when it is turned on. Side effect: while the extension is off, `IsUnlocked("mod_builtin")` is still true; remove the builtin from `Functions` to hide it.
- **Unknown item names are dropped**: `ItemBlock.Deserialize` skips names with id < 0 (U:~1870). If the item SOs are not registered, mod item counts are lost at the next save. This is why §3.1 says to always register mod items while the plugin is installed. Also mirror their counts to a sidecar file for the case where the plugin is uninstalled.

### 5.2 Code files
- `Saver.WriteCodeFiles(Workspace, savePath, backupPath)` (C:10929) writes `<fileName>.py` for every code window. It then **deletes every `*.py` in the save folder that is not an open window** (except `__builtins__.py`). It writes `__builtins__.py` from `StreamingAssets/Languages/builtins.py` if that is missing.
- `Saver.Load` (C:11028) reads `*.py` (excluding `__builtins__.py`) and calls `workspace.OpenCodeWindow(name, code, pos, size)`.
- The `.py` extension is hardcoded in **4 places**: `WriteCodeFiles`, `Load`, `OnFileChanged` (C:11157; the file watcher for external editors) and `UpdateFromOldSaveGame`.
- File names: `Workspace.GenerateFileName("f")` → f0, f1… (C:16698). `IsValidFileName` (C:16711) rejects duplicates (case-insensitive), `__builtins__` and invalid path characters.
- Recommendation for (A): keep `.py` on disk; the game logic, backups and the watcher all assume it. Store each file's language in one of two ways:
  - a first-line marker comment the compiler strips, e.g. `// @lang rust`. This is robust and travels with the file.
  - the sidecar file

  External-editor extensions (.rs/.c) would need patches at all 4 sites plus a file name map. Defer that.

### 5.3 Per-save mod data
- Write `Saves/<name>/javafarmer.json` in the existing `WriteCodeFilesPostfix`. It receives `savePath` and `backupPath`; write to both. Only `.py` files are deleted, so the sidecar is safe.
- Read it in a postfix on `Saver.Load`, or a prefix on `MainSim.SetupSim`. Note that `SaveChooser.Setup` lists directories, and `DeleteSave`/`RenameSave` move the whole folder, so the sidecar follows.
- Suggested content:
  - enabled extensions (if per-save rather than global)
  - per-file language
  - mod item counts (backup)
  - extension state such as "threat level" or "model accuracy"
- Naming pitfalls:
  - Mod unlock names must be lowercase (`Farm.Unlock` lowercases, `NumUnlocked(UnlockSO)` does not).
  - They must not end in `_<digits>`, or they would be parsed as levels.
  - They must not start with "debug".
  - They must not clash with vanilla names or keywords.

## 6. Docs system

- Paths are relative to `StreamingAssets/Languages/<LANG>/`, e.g. "docs/home.md" or "docs/unlocks/senses.md". `Localizer.LoadDoc(doc)` (U:9545) returns the file text, or "" if the file is missing. The EN docs are 48 files: 9 top-level, 16 scripting, 23 unlocks. builtins.py and LICENSE (CC0) are shared.
- `DocsWindow.LoadDoc(string doc)` (C:13061) recognizes virtual prefixes:

  | Prefix | Content |
  |---|---|
  | `functions/<name>` | `code_tooltip_<name>` + "unlocked in" |
  | `unlocks/<name>` | `UnlockTooltip` |
  | `items/<name>` | `ItemTooltip` |
  | `objects/<name>` | `FarmObjectTooltip` |
  | anything else | `Localizer.LoadDoc` |

  Links: `[text](docs/x.md)`; `https…` opens the browser; `persistent_data_path/…` opens a folder.
- `MarkdownText.Setup` (C:14051) pipeline:
  1. `{{…}}` texts (`InsertCustomTexts`, C:14283): `unlocksTOC`, `builtinsTOC`, `itemsTOC`, `entitiesTOC`, `groundsTOC`, and `{{@key}}` (localized string)
  2. `ApplyCodeTags`
  3. headings `#`
  4. links
  5. `<unlock=name>…</unlock>` (shown only if `MainSim.Inst.IsUnlocked(name)`)
  6. `<spoiler=…>`
  7. images `![](SpriteName<height>)`
  8. custom sections `{{output}}` and `{{itemblock stats_sum|stats_best|tooltip|cost object <x>|cost unlock <x>}}`
- TOC: docs/home.md has sections for general info and programming (hand-written links wrapped in `<unlock=…>`) plus the generated unlocks, builtins, items, entities and grounds TOCs. There is no separate tree object; home.md *is* the TOC.
- JavaFarmer `ContentStore`: `LoadDocPostfix` replaces the result with `content/<doc>` whenever that file exists, **including paths the game does not have**. This works for every UI language when `ContentForAllLanguages` is set. It also has path-traversal protection.
- Adding chapters:
  1. Create content/docs/mods/cybersecurity/*.md and content/docs/mods/ai_ml/*.md. Point the `UnlockSO.docs` fields at them; a page opens automatically on first unlock.
  2. Override home.md (already in content/) and add `## Mods` with `<unlock=cybersecurity>[Cybersecurity](docs/mods/cybersecurity/index.md)</unlock>`. Because of the `<unlock=>` gating, the section only appears once the root mod unlock is bought.
  3. Mod items and entities appear in the generated TOCs automatically, filtered by `enabled` and by unlock.
- Multi-language (A): docs and tooltips are Java-specific today. Plan for content/<lang>/docs and content/<lang>/Strings and have `ContentStore` select by the current code-language option. Re-run `Localizer.LoadLang` to re-merge strings; `ReloadStringsIfAlreadyLoaded` in Plugin.cs shows how. Reload open docs windows, e.g. by firing `OptionHolder.OnOptionChanged("language")`, which `DocsWindow.OnSettingChanged` handles.

## 7. Leaderboards and achievements

- JavaFarmer already zeroes `SteamLeaderboard.LoadLeaderboard` scores and blocks `SteamUserStats.UploadLeaderboardScore`. Keep that for all languages and extensions. Leaderboard "everything unlocked" runs include mod unlocks (§2.3), and Python passthrough is still "modded".
- `Achievements` (C:28):
  - `public static volatile bool enabled` (C:66)
  - `LoadStats` (C:68) reads Steam stats for `trackStats` items from `Resources.LoadAll<ItemSO>` only, so mod items are excluded.
  - `CollectItem` (C:119) runs from `Farm.CollectItems` only for `item.trackStats`. It does `SetStat(itemName)` and `SetStat("total_"+itemName)`, so **mod items must use `trackStats = false`**, or the game writes non-existent Steam stats.
  - `UnlockAchievement` (C:274) is called from many places (PLANT_*, EXPAND, USE_MULTIPLE_DRONES, SWARM, HIGHER_ORDER_PROGRAMMING, CHAOS, STACK_OVERFLOW, CAUSE_A_RUNTIME_ERROR, UNLOCK_EVERYTHING); all check `leaderboardType == none`.
- `ResearchMenu.Update` fires UNLOCK_EVERYTHING when every *displayed* box is maxed. With extensions on, the player must also max the mod nodes. Mod nodes never make it fire *earlier*.
- `UnlockBox`: a level above max (TooHigh) sets `Achievements.enabled = false`. `ButtonClicked` resets the levels and sets it back to true.
- If extensions should not earn Steam achievements, prefix `UnlockAchievement`/`CollectItem`/`IncrementStat` with "return false while an extension is enabled". Do not just set `enabled`, because `UnlockBox` overwrites it.

## 8. Other relevant facts

### 8.1 Localization
- `Localizer` (U:9466): a `Localize(key)` call on a missing key returns the key itself.
- Strings live in `StreamingAssets/Languages/<LANG>/Strings/*.txt` (14 files: buttons, button_tooltips, code_tooltips, execute_errors, hat_tooltips, item_tooltips, languages, leaderboard_tooltips, object_tooltips, options, other, parse_errors, popup_warnings, unlock_tooltips, warnings).
- `ApplyCodeTags` (U:136) is applied to values.
- UI labels use `TMPTextLocalizer` (U:12141): the text is the key, re-localized on change.
- Keys a mod needs:
  - unlocks: `<unlockName>` (title), `unlock_descr_*` (the `description` value is the key itself), multi-level descriptions
  - items: `item_descr_*`
  - objects: `object_descr_*`
  - builtins: `code_tooltip_<name>`
  - option tooltips
  - mod error keys (any key string passed to ExecuteException)

### 8.2 unlock_display_code
- `UnlockSO.displayCode` is the Python snippet drawn on text-style unlock boxes (mesh == null): while, unlock(), set_speed(), get_time(), get_cost(), '?', import, random(), {1:2}, (x,y), a = 2, print(), 1 + 1, simulate(), [1,2,3], def f():
- It is rendered with `CodeUtilities.SyntaxColor2`. JavaFarmer patches that function for Java colouring.
- Content maps unlockName → snippet in content/unlock_display_code.txt. For multi-language support this becomes per-language. The `UnlockBoxPrefix` writes the snippet into the SO once (`ConditionalWeakTable` guard), so a language switch must reset the guard or keep the original snippets.

### 8.3 CodeWindow
- `CodeWindow` (C:11971): `fileName`, `CodeInput` (CodeInputField), `parsedFunctions`, `cachedProgram`. `Parse()` (C:12788) → `Parser.Parse(TokenStream, CodeWindow)` (C:9149) is JavaFarmer's compile hook, the place to dispatch by language.
- `Scope.Evaluate` (C:~10440) gives "missing import" hints by looking at other windows' `parsedFunctions`.
- Module names are file names; `import f0` / `from f0 import x`.

### 8.4 The game's Python dialect
- Tokenizer (C:10564) and TokenType (C:10806). Keywords: def, if, elif, else, for, in, not in, while, and, or, not, pass, break, continue, return, global, import, from, True, False, None.
- Assignment: `=`, `+=`, `-=`, `*=`, `/=`, `//=`, `%=`.
- Operators: `+ - * / // % **`, comparisons, `[]` indexing and slicing with step, `.`. Unary: `+`, `-`, `not`.
- Strings: `'…'` or `"…"`, **no escapes**, single line. Comments: `#`. Numbers: decimal only.
- **Not supported**: classes, lambda, try/except, with, yield, is, del, bitwise operators, f-strings, comprehensions (no parser support found; **(guess)** none).
- `+` concatenates lists, strings and tuples. `*` is numbers only.
- Equality and comparison are deep (`CodeUtilities.DeepEquals`/`DeepCompare`, cost = steps).
- Function definitions allow default parameter values. `global` exists.
- Builtins:
  - **Pure:** range, len, min, max, abs, str, random() (no seed; a `System.Random` per state), list(), set(), dict(), get_tick_count, quick_print
  - **Methods:** append, add, remove (list/set), pop (list/dict), insert. There are no string methods, no sorted/sum/int/float/round/enumerate/zip/type/isinstance, and no math module.
  - **Farm:** harvest, can_harvest, plant, till, move, can_move, swap, use_item, get_water, get_pos_x/y, get_world_size, get_entity_type, get_ground_type, get_companion, measure, num_items, get_cost, clear, unlock, num_unlocked, change_hat, do_a_flip, pet_the_piggy, print
  - **Meta:** get_time, set_execution_speed, set_world_size, simulate, leaderboard_run, spawn_drone, num_drones, max_drones, wait_for, has_finished
- Constants: `Items`, `Entities`, `Grounds`, `Unlocks`, `Hats`, `Leaderboards` (`PyConstBag`, case-insensitive member lookup, `Items.WEIRD_SUBSTANCE`), and `North/East/South/West`.
- Limits: list()/set() inputs up to 100000 elements; call depth up to 1000 (`error_max_stack_size_reached`); `Logger` output up to 100 lines × 1000 chars; at most 36 drones (`MessageQueues[36]`).
- Every target language (C, C++, Rust, JS, Java, Zig, C#, R) therefore lowers to: double-only numbers, list/dict/set/tuple, functions with closures via `parentScope`, no exceptions and no objects. That is the same constraint set the Java compiler already handles (SPEC §6). The "Python" choice should bypass the compiler (vanilla parse).

### 8.5 Other notes
- `Farm.allKeyWords` (C:2903) controls which keywords enter `unlockedKeyWords` and therefore completion. Mod constant namespaces (e.g. a new `Threats` bag) would need a postfix on `Scope.EvaluateConstant` (static, C:10490; also used by `IsConstant`), plus completion (`GetSubWordList` postfix; JavaFarmer already patches it).
- `Simulation.EvaluateName` (C:4966) and `MainSim.EvaluateName` are hover-value lookups (already patched).
- **Hook order for the extension bootstrap:**
  1. Plugin `Awake` registers patches.
  2. `Menu.Start` → `ResourceManager.LoadAll`, with the `StringIds.SetItemIds` prefix adding items and the `LoadAll` postfix adding FarmObjectSOs and UnlockSOs.
  3. `Achievements.LoadStats`
  4. `Saver.Load` → `MainSim.SetupSim` → `new Simulation` → `Farm` ctor (postfix: timers) → `researchMenu.Setup`
  5. `Menu.Start` postfix adds the UI buttons.
  6. Builtins are added to `BuiltinFunctions.Functions` according to the enabled extensions, at any point before the first execution. Re-apply them on toggle.
