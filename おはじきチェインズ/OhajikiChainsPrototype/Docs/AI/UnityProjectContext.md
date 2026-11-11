## Current request 20261111_03 — 2026-11-11

Follow-up version 1.005 fixes charge overlap with the player shot/input area. Charge radius is 22 (diameter44), vertical centers span .22-.78 of board height, and rim/shine/label geometry scales with the configured radius. The bottom rim is y120.185, above the full input strip ending at107; the top rim is y529.815, below enemy launch zone548. Existing geometry and built-player checks now cover visible rim bounds against both shot areas, not only the smaller ball footprint. Output remains `Build/20261111_03`. See `../20261111_03_ショット領域調整.txt`.

Version 1.004 implements the explicit prompt ahead of the workbook: no screen or damage-digit oscillation, 8x8 charges with non-overlapping spacing, and a gold 全消し banner scrolling right-to-left for 1.5 gameplay seconds. `ShotSimulation.AllCleared` fires once per emptied board (including missile chains), then waits 5 simulation seconds and refills only if the shot is still moving. It preserves projectile state/hit totals, supports another clear, and cancels the timer on stop/retry. Pause advances neither timer nor banner. `PrototypeBattle` refreshes charge visuals immediately after a mid-shot refill. Defaults/assets agree; legacy shake fields remain hidden for serialization compatibility and no longer drive rendering. Build output: `Build/20261111_03/OhajikiChains.exe`. `BoardRulesTests` and explicit `BoardSmoke` cover the new behavior; `RunSmoke.ps1 -Mode presentation` now runs board, feedback, title and hub. See `../20261111_03_変更と検証.txt`.

## Previous charge-ball specification revision — 2026-11-11

Version 1.003 follows the revised sheet `12_チャージボール`: five special types are 5% each and warp is 10% (normal 65%). Both `BattleTuning.cs` defaults and the authored asset are updated. `ShotSimulation` remembers each side's successful launch position; warp targets the opponent's last launch, falling back to the opposing center before their first shot, and preserves velocity. Preparing a turn does not overwrite launch history. The 18-sheet data master still holds older, unconfirmed values; it was not modified or imported. Build output remains `Build/20261111_01`. See `../20261111_チャージボール変更と検証.txt` for validation.

## Previous request 20261111_01 — 2026-11-11

Version 1.002 targets the current workbook and request. Historical notes below are superseded where they conflict.
- Output: `Build/20261111_01/OhajikiChains.exe`, Unity 6000.3.21f1 at `C:/Program Files/Unity/Hub/Editor/6000.3.21f1/Editor/Unity.exe`, Windows x64 / Mono / D3D11.
- `QuestCatalog.asset` contains 3 main, 3 event, 3 showdown groups × Easy/Normal/Hard, 4 material, 5 sequential challenge and 1 practice variants. All definitions/stats/rewards/dates are Inspector-editable provisional values.
- `PlayerSession.Quests` owns energy, rank-derived capacity, run snapshots, clear flags and idempotent rewards. `QuestRun` captures the definition and participants. Completion is committed once; losses/abandonment award nothing. Progress still resets on exit.
- `BattleLoadout` owns cloned quest enemies as well as the selected player deck. `PrototypeApp` connects quest entry/result/retry/return; legacy direct entry remains available to existing smoke modes.
- Practice keeps running at enemy HP0; only retirement clears it. The defeated enemy stops acting. Practice has no energy cost or rewards.
- UI: home → category → scrollable list → detail/difficulty → entry confirmation. Event/showdown banners, disabled multi/mission placeholders, header energy refill, settings/legal view restored. Missing `SpecificationSmoke` is now implemented.
- Battle digits follow the latest spreadsheet's yellow-to-red progression. Pink burst geometry and 500/1000 size thresholds remain; only digits shake. This supersedes the historical fixed-red instruction below.
- `dataInput_list.xlsx` was replaced externally during this task with an 18-sheet master (schema 1, data version 20261111_02). Its rows were unconfirmed and its guide says import is not connected. Preserve it. `DataInputImporter` detects this format and records that builds use existing Inspector data; manual import rejects it. The legacy four-column importer still supports empty workbooks and staged/validated target/id/field/value overrides, but is NOT an adapter for the new master. See `../20261111_01_仕様対応とデータ入力.txt`. This task has not modified any XLSX.
- New editor assembly `Ohajiki.Editor` contains build/bootstrap/import tools. Tests reference it; new QuestTests cover content, locking, progression, reward atomicity, snapshots, practice, energy, and XLSX parsing.
- Validation evidence and remaining limits are recorded in `../20261111_01_検証結果.txt`; `Tools/RunSmoke.ps1` runs each built-player smoke explicitly. Do not run players concurrently.

## Previous specification update — 2026-11-11

The 2026-11-10 spreadsheet and user approval override historical decisions below.
Current implementation is version 1.001. See ../2026-11-11_仕様対応と検証.txt for the current coverage and validation record.

- Refill only cleared charge slots after each side's action. Preserve active slots and their element/special kind.
- First-single discount is once per banner, session-scoped. GachaCost and FirstSingleUsed now receive the banner index.
- All six special charge effects are implemented in ShotSimulation, including weighted normal contacts, independent translucent split projectiles, missile cascades, warp, and action traps/splits.
- PrototypeBattle uses weighted contacts and combatant buffs, handles lethal traps before staged resolution, and grants character XP once via PrototypeApp and captured BattleLoadout.SpiritIds.
- Added AttackUp, SpeedUp, Cleanse, AttackDown, SpeedDown with durations measured in the affected side's completed actions. Existing enum IDs are preserved.
- PlayerSession tracks character XP. Provisional requirement is 100 + 25*(level-1), full battle reward for each unique participant, cap60. Inspector-configurable in HubConfig.
- Initial catalog examples are configured in Spirit_00/01/02/04/05/07/08/10 assets. Original stats, artwork and GUIDs are retained.
- Latest screenshot request (2026-11-11): the upper-right damage bank is a pink irregular comic burst with a dark border and red, clockwise-tilted digits only. Its center is fixed; displayed damage below 500 uses 55% size, 500–999 uses 78%, and 1000+ uses the reference maximum (380×280). Each shot resets to small. Digits still shake/scale; fixed red supersedes the earlier yellow/orange/red transition. The upper-left pink rectangle shows ノーマルスキル and a large black LV using the existing effect-level calculation and left slide. Enemy information is now a wide horizontal bar with separate HP/status rows; damage flights and flashes target the new HP position. The updated Windows build and FeedbackSmoke passed with exit 0, including 499/500/999/1000 boundaries and 540×960, 480×800, 720×900 layout checks. See ../2026-11-11_戦闘UI修正.txt and Logs/ReferenceUI-*.log. No shot trail.
- Filter and sort have explicit selection dialogs. Settings shows gameplay/special-ball information. Legal Notice is configurable and reachable from Settings and purchase confirmation.
- User explicitly approved simulated purchases on 2026-11-11. Shared gems and session-only progress remain the current prototype model. Apple/Google linking stays disabled as explicitly allowed in the specification.
- New StatusProgressTests and SpecialChargeTests cover status duration, weighted damage, XP, special effects, traps, knockout cancellation, and preserved refill slots.
- Unity Hub was authenticated and native EditMode tests passed119/119 on both6000.3.14f1 and6000.3.21f1. The old engine reproduced a Windows UIAutomation shutdown crash (UUM-146676) and a D3D12 allocator crash. Installed6000.3.21f1 side by side under C:/Users/yutoi/AppData/Local/Unity/Editors and use D3D11 for Windows builds. See current validation report for final runtime results.
- PrototypeBuilder.Build successfully produced Build/Windows-v8/OhajikiChains.exe with Unity 6000.3.21f1 and D3D11. All six final runtime smokes (feedback, spec, hub, title, shop, gacha) passed and exited 0. Normal title-to-home click and Alt+F4 also exited 0. Final review enlarged the result body to fix UI OVERFLOW; rebuilt and reran the affected hub/spec smokes with PASS, exit 0, and no overflow, then visually checked the result screenshot. See Logs/Final-*.log and the current validation report. A ComputeBuffer GC disposal warning remains at shutdown; no first-party code uses ComputeBuffer directly. Mobile devices remain untested.
- Editor upgrade migrated bundled 2D/Input/Rider packages and Unity project settings; URP remains 17.3.0. Session state resets on application exit; billing is simulated as approved.

The remainder records the previous version and historical validation.

# Unity project context

Analyzed: 2026-11-09. Updated local prototype, no source-control commit created.

## Confirmed environment

- Project: `おはじきチェインズ/OhajikiChainsPrototype` (new project; unrelated sibling projects untouched).
- Unity 6000.3.14f1, Universal 2D template 6.1.2, URP 17.3.0.
- Input System 1.19.0, uGUI 2.0.0 / TextMeshPro, Test Framework 1.6.0.
- No network gameplay, saves, remote services or Unity MCP installed. Unity CLI and installed Editor available.
- Runtime targets Windows x64. Touch-compatible pointer code exists but mobile devices have not been tested.

## Structure and startup

- `Assets/Ohajiki/Scripts`: runtime assembly `Ohajiki.Runtime`.
- `SpiritDefinition`: configurable character stats, growth, skill and portrait. Neutral is forbidden for spirits.
- `BattleRules`: contact damage = attack * contacts * configurable 0.15. `ResolveNormal` applies normal HP loss and snapshots mitigation; `ResolveSkill` applies skill effect and completes the action. Fixed guard budget and percentage rounding are shared; duration is consumed once. `Resolve` is the combined compatibility API. A pending action must finish even after a lethal normal hit.
- `PlayerProgress`: session-only rewards, coin spending, and extensible rank bands. No persistence yet.
- `PrototypeApp` / `PlayerSession`: startup title → home, owned counts/levels/materials, selected 12-spirit deck, shared rewards and energy. One active screen/EventSystem. Title owns its input until it is disabled/destroyed; home is created once on start.
- `TitleView`: specification 7.2 title, animated existing character art/logo/orbs, tap-to-start fade, Application.version (initial 1.000), X link to https://x.com/don_kng68, and a backup modal with disabled Apple ID / Google buttons. Close restores the title. No account SDK, saves or SE. TitleSmoke tests normal startup; existing named smoke modes explicitly bypass it.
- `HubView` / `HubWidgets` / `HubShopView`: runtime uGUI home, deck editing, upgrades, gacha and shop. Only settings remains a disabled button.
- `HubConfig.asset`: provisional initial inventory, 3 decks, 4 specification gacha types/rates, and new specified costs: first single 1 gem, normal single 5, ten 50 plus 1 bonus. User confirmed first-single discount is global once, and paid banners use the shared gem wallet in this prototype. 12 initial owned spirits from a 24-spirit catalog, level cap 60. FeaturedIndices rotate every 5 seconds.
- `PlayerSession.Gacha` owns one atomic consumption/draw/grant. Immutable `GachaReceipt` drives all playback; skip/next never reroll or grant. Confirm is latched against duplicate invocation. First-single usage is session-only; ten draws do not consume it. Free draw API/UI removed.
- `HubGachaView` / `GachaSequence` / `GachaStage3D`: confirmation, featured carousel, real-time 3D camera approach, orb drop/release, full-body reveal, individual ten reveals and summary. Single skip goes to reveal, ten skip goes to summary. Next returns to gacha home. 3D set is isolated at x=10000 on layer 31, rendered to a 540x960 RenderTexture. No global pipeline/layer/camera edits. Stage owns and destroys camera, texture, meshes/materials on close or interruption. No SE.
- `SpiritBallVisual`: faint portrait inside glossy masked spirit orbs (battle projectile/enemy and gacha results). Charge balls remain element-only.
- `ShopConfig.asset` / `ShopState` / `PlayerSession.Shop`: exchanges and simulated purchases, shared product limits, sale/event windows, confirmed quote applied once, monthly premium and one daily 2-gem reward. Dates use Japan time. No billing SDK/network. State remains session-only.
- `BattleLoadout`: per-entry clones of selected spirits and levels; source assets remain immutable. Disposed on return or retry.
- `ShotSimulation`: deterministic substepped axis-aligned square reflection/collection, canvas-unit coordinates, no Physics2D dependency. `squareHitboxes=false` retains the old circle comparison mode.
- `PrototypeBattle`: shot → charge/lock total → DamageFlight (HP unchanged) → NormalImpact (normal HP loss) → cut-in → Impact (skill) → reset/next side. Pause/retry and hand long-press retained. DamageFlight is appended to the enum to preserve existing values.
- `BattleView`: generated uGUI hierarchy, TMP Japanese text, supplied portraits, upper-right damage bank, separate normal/skill result text, energy/cut-in effects, skill details, fit-to-window canvas.
- `BattleFeedback`: bounded pooled double rings, stars/shards/numbers, flying motes, trail, chain milestones and stronger position/angle/size oscillation of the damage bank. A numbered comet flies from the bank to the target. Impact readout uses a dark band with large contrasting digits. Pause freezes effects; restart clears them. No gameplay authority. All SE code remains removed.
- `SpiritUiSkin`: shared sunny ruins/pale stone/cream and gold theme for all hub and battle screens. Owns procedural sprites/textures per canvas and destroys them on exit. Two new generated illustrations are in Resources/UI; original character art is unchanged. FeedbackOutline material retains the mobile TMP outline variant in player builds. `UiButtonMotion` provides visual press/hover feedback without changing button actions.
- `Data/Spirits`: 24 provisional characters including three each at rarity 4 and 5; existing portraits reused. `Data/Enemies`: separate 12-character assets. Existing characters are not overwritten.
- `BattleTuning.asset`: explicitly provisional parameters; original spreadsheet is unchanged.
- `Assets/Ohajiki/Editor/PrototypeBuilder.cs`: creates missing data once, preserves scene GUID, builds Windows-v7.2. Sets PlayerSettings.bundleVersion to 1.000 for the first title release; update this value for future releases. Partial authored decks fail validation without replacement. Shop migration 20261106 retained. Presentation/gacha migration 20261109 sets stronger feedback, travel timing, gem costs and provisional multi-featured characters once. Later rebuilds preserve Inspector edits.
- `Assets/Ohajiki/Scenes/Prototype.unity`: camera, configured PrototypeApp, original battle component retained but disabled. App creates the runtime battle. UI is instantiated at runtime.
- `Assets/Ohajiki/Tests/Editor`: NUnit `Ohajiki.Tests`, covers specification boundaries, deck invariants and collision/turn rules.
- Font is RocknRoll One from workspace OpenSiv3D example, distributed with its OFL.txt. Character copies originate from user's existing assets.

## Constraints

Read `../2026-11-05_更新設計.txt` for current source-cell references, assumptions and design. The user confirmed full board reset after EACH side's action.
Do not represent prototype values as approved specification. Preserve the spreadsheet and original artwork.
Read `../2026-11-05_ホーム設計.txt` for home flow and provisional leveling. Current gacha overrides the old free trial behavior; see `../2026-11-09_演出ガチャ更新設計.txt`. No real monetization, account linking or permanent progression. Duplicate copies are counts, not separate levelable instances. Battle experience only raises rank; character XP distribution remains unspecified. Energy has no timed recovery yet. All progression resets on app exit.
Read `../2026-11-06_ショップ更新設計.txt` for shop assumptions. Purchases grant rewards after confirmation without real payment. User approved each release pack's provisional 20 matching powers + 10 gems. Premium purchase/claim history and limits reset on exit. Gacha now consumes the specified shared gems; paid/free gem provenance is not separated by the user's explicit choice.
No need for MCP installation to build/test. Setup updates the existing scene, so save unrelated open-scene edits first. Do not call Setup merely to inspect the scene.
Read `../2026-11-07_接触演出設計.txt` for action order and `../2026-11-08_UI演出改修.txt` for current styling. The damage bank is pre-guard normal damage, not total normal+skill damage. Impact text is actual HP change. Contact celebration thresholds are visual-only and independent of skill level thresholds. Feedback timings, intensity, bank shake and tilt are in BattleTuning. SE is removed, including generated audio sources/clips. Proposal PDF pages 1–3 guide the cheerful ruins style; latest spreadsheet and confirmed rules take precedence over older concept rules.

## Validation

See historical validation documents and `Docs/2026-11-09_演出ガチャ検証結果.txt`. Run `unity test` in EditMode (96 passing cases as of this update).
Build method: `PrototypeBuilder.Build`; output `Build/Windows-v7.2/OhajikiChains.exe`. Previous builds through Windows-v7.1 remain intact.
`--title-smoke-test` checks startup/version, 3 window sizes, backup modal/provider placeholders/input blocking/repeated close, duplicate tap protection, title cleanup, home/battle transitions, unchanged session/energy and one active EventSystem. It checks the X URL but does not launch an external browser. Screenshots: SmokeTitle.
`--gacha-smoke-test` verifies costs/cancel/global discount, carousel, real 3D rendering/camera motion, four-stage playback, all ten reveals, single/ten skip, duplicate confirmation/skip protection, cleanup on interruption, and no SE. Records screenshots and first-summon frames under SmokeGacha.
`--feedback-smoke-test` validates actual phase order and HP timing on both sides, contact counts, damage bank, pause, reset, retry and home cleanup. Captures under `Smoke/Feedback`, including first-action frames. Uses a runtime cloned attacking skill fixture; no source asset edits. Never run smoke modes concurrently.
`--shop-smoke-test` validates exchange/purchase/cancel/gem balances/shared pack limits/premium and the new gacha rates UI; writes SmokeShop screenshots. Avoid running smoke modes concurrently.
`--hub-smoke-test` exercises home/navigation/modals/gem-cost summon/deck/upgrade/battle-return, funds gems through simulated shop purchases and uses an enemy-HP terminal fixture to verify rewards. Captures SmokeHome screenshots. `--stay-open` keeps the app open after that test. Normal startup never attaches HubSmoke.
Runtime smoke mode is explicit `--smoke-test`, saves screenshots under the executable's `Smoke` folder and exits with status 0 on success. Hidden-window screenshots may be black; use real window captures for visual evidence.
Player swipe thresholds do not restrict enemy AI shots. Both sides still require a nonzero direction toward the board.
Known shutdown issue: v2/v2.1 and the first home smoke run return 0xc0000005 after successful gameplay assertions. Installed Unity PDB mapped the v2.1 fault to `RuntimeStatic<PlatformAccessibilityManager,0>::StaticDestroy +0x51`; root cause remains unconfirmed. Final home build completed HUB SMOKE PASS and exited 0, but that single successful shutdown does not establish a fix. ComputeBuffer shutdown warning remains. See the home validation and follow-up reports.
UiSmokeChecks verifies active visible button centers using EventSystem.RaycastAll, text overflow and zero AudioSources. Hub smoke covers 540x960, 480x800 and 720x900. These do not replace human pointer playtesting or physical mobile-device validation.

Damage-bank visibility patch (2026-11-09, v7.1): BattleView.Refresh shows the bank only from PlayerShot/EnemyShot through Impact, including total lock, flight and cut-in. Ready, EnemyThinking, Finished and ResetFeedback hide it. Pausing an in-progress action retains its frozen bank. FeedbackSmoke and HubSmoke check start/next turn/retry/enemy wait/result visibility.

Title/color patch (2026-11-09, v7.2): Bank background follows the rolling displayed pre-guard damage: yellow → orange → soft coral, capped at BattleTuning.damageColorFullAt (provisional 600, midpoint 300). Both sides start yellow each shot. Golden rim and white/cream digits remain; no damage/phase changes. SpiritUiSkin.DamageBurst is a separate neutral-shaded sprite, preserving other burst art. FeedbackSmoke uses an isolated presentation fixture for color progression/cap/reset, then validates the actual battle as before. Title and feedback smoke runs passed with exit 0; build has no compiler errors. See ../2026-11-09_タイトル画面とダメージ色.txt. No new EditMode test run for this presentation-only patch.
