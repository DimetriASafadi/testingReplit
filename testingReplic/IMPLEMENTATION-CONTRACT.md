# New Gaza first-playable implementation contracts

Existing Unity race scene stays unchanged. New native Unity scene: Assets/NewGaza/Scenes/NewGaza.unity. Unity 6000.3.18f1 / URP / Input System / UGUI. No external dependencies in game domain.

## Core (NewGaza.Core)

Models are defined in Assets/NewGaza/Core/GameModels.cs; do not change public fields without coordination.

GameCatalog static members:
- DistrictDefinition[] Districts (10 districts, plus final Al Rashid at index 10)
- long FactoryCost = 15000, ExcavatorCost = 8000, TruckCost = 6000, BulldozerCost = 5000
- GameState CreateNew(long now)

EconomyService instance:
- EconomyService(GameState state); public GameState State {get;}
- void Tick(long now) handles offline timestamps and automatic clearing -> hauling -> recycling completion.
- ActionResult SelectDistrict(int index)
- ActionResult BuyEquipment(string kind, long now) kind = factory / excavator / truck / bulldozer
- ActionResult UpgradeFactory(long now) levels max5
- ActionResult UpgradeEquipment(long now)
- ActionResult StartSalvage(int district, long now)
- ActionResult SellResources(string kind, long now) kind concrete/iron/wood/other/all
- ActionResult StartProject(int district, string projectId, long now)
- ActionResult CollectIncome(int district, string projectId, long now)
- ActionResult ClaimDistrictReward(int district, long now): MUST require 100%, one-time reward, next district unlock only after claim. Rashid only after all ten claimed.
- ActionResult ClaimDailyGift(long now): 24h, no wheel/gambling, guaranteed coins + materials scale with completed districts.
- float Progress(int district) [0..1]
- long PendingIncome(int district, string projectId, long now)
- bool CanClaimDailyGift(long now)
- ProjectState FindProject(int district, string projectId)

All money/resource validation authoritative in service, failure returns Arabic message. Times Unix seconds. Call Tick before actions; clock rollback must not create income/gifts. Projects use real durations (small house 25k/2h; road150k/18h). Include affordable repeatable investment to avoid an economic dead end; after local rubble cleared, imported recycling contracts can provide resources but do not increase district completion. Clearly labeled in UI. No paid ad rewards without SDK verification. No silent state reset on save corruption.

## Runtime (NewGaza)

Main agent owns GameSession MonoBehaviour:
- public static GameSession Instance
- public EconomyService Economy {get;}
- public GameState State {get;}
- public event Action Changed
- public event Action<string> Notification
- public bool Ready {get;}
- public string SaveError {get;}
- public long Now {get;}
- public void Perform(Func<EconomyService, ActionResult> action) (save, events, notification)
- public void ChooseDistrict(int index)
- public void SelectPlot(int index) fires PlotSelected event Action<int>
- public event Action<int> PlotSelected
- public void Notify(string message)
- public void Save()
- public void SetPlayerName(string value)

## World (NewGaza)

World agent owns CityWorld MonoBehaviour + supporting visual types:
- public void Initialize(GameSession session)
- public void Refresh() on Changed, update visuals incrementally, no rebuild each second.
- public void FocusDistrict(int index)
- public Vector3 DistrictPosition(int index)
- public void SetSelectedPlot(int index)
- public void PlayFinale()
- CitySelectable MonoBehaviour fields public int districtIndex; public int plotIndex=-1 (district select when -1; project index when >=0; use -2 for rubble / -3 recycling factory)

Main owns CityCamera MonoBehaviour:
- public void Initialize(GameSession session, CityWorld world)
- public void Focus(Vector3 point)
- public void FrameCity()
- public void PlayFinale()
- handles input touch/mouse tap, second tap same selection context, long press inspect, one finger pan, two finger pinch/rotation, UI exclusion and gesture thresholds.
- when selecting project, session.SelectPlot; double tap executes project or collects income through session.Perform. Rubble -2 starts salvage; factory -3 opens UI via plot event. Never act on locked districts.

## UI (NewGaza)

UI agent owns CityHud MonoBehaviour and ArabicText helper:
- public void Initialize(GameSession session, CityWorld world, CityCamera camera)
- build Arabic RTL UGUI interface responsive safe area, touch targets, EventSystem InputSystemUIInputModule
- Font loaded Resources.Load<Font>("NewGazaArabic") (main supplies compatible TTF and license). No Arial.ttf.
- show balance/materials/progress/active project timers/job phase/tutorial
- navigation tabs city map (11 districts lock/rarity/progress/reward), projects, fleet + factory upgrades, investments, resources sell/storage, daily gift
- project selection via session.PlotSelected, contextual panel show cost/duration/prereq/resources/progress/completion/income; show explicit action confirmation for spending; initial equipment onboarding
- toast errors, no negative balances, no ad simulation; ad button explicitly unavailable (real provider not connected)
- production real project durations. Do not expose grant-money / time-travel controls in normal gameplay.
- screen capture optional main implements CityCapture with public void Capture(GameSession session, CityCamera camera) and screenshot includes HUD summary; video recording not implemented.

Agents only edit their owned files/directories. Main handles .meta + scene + build defaults and docs; core agent may add .NET domain tests outside Assets. Notify main of contract changes.