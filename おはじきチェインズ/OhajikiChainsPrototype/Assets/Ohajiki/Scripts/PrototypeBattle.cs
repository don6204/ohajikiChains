using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Ohajiki
{
    public sealed class PrototypeBattle : MonoBehaviour
    {
        [SerializeField] BattleTuning tuning;
        [SerializeField] TMP_FontAsset japaneseFont;
        public BattleView View { get; private set; }
        public ShotSimulation Simulation { get; private set; }
        public BattleState State { get; private set; }
        public SpiritDeck Deck { get; private set; }
        public SpiritDeck EnemyDeck { get; private set; }
        public PlayerProgress Progress { get; private set; } = new PlayerProgress();
        public event System.Action HomeRequested;
        public event System.Action RetryRequested;
        public event System.Action<int> ExperienceEarned;
        bool embedded;
        public BattlePhase Phase { get; private set; }
        public int SelectedSlot { get; private set; }
        public bool Paused => paused;
        public bool Resolving => Phase == BattlePhase.Charging || Phase == BattlePhase.DamageFlight || Phase == BattlePhase.NormalImpact || Phase == BattlePhase.CutIn || Phase == BattlePhase.Impact;
        public ActionResult LastAction { get; private set; }
        bool dragging, paused, showChain, enemyActing, rewardGranted;
        int enemySlot, heldSlot = -1;
        float phaseTime, heldTime;
        System.Random enemyRandom;
        SpiritDefinition actingSpirit;
        QuestDefinition quest;
        System.Func<bool,QuestReceipt> completeQuest;
        public void ConfigureQuest(QuestDefinition definition,System.Func<bool,QuestReceipt> complete)
        {quest=definition;completeQuest=complete;}
        public SpiritDefinition PlayerCard => tuning.playerDeck[Deck.CardAt(SelectedSlot)];
        public SpiritDefinition EnemyCard => tuning.enemyDeck[EnemyDeck.CardAt(enemySlot)];
        public void Configure(BattleTuning config,TMP_FontAsset font) { tuning = config; japaneseFont = font; }
        public void ConfigureSession(BattleTuning config,TMP_FontAsset font,PlayerProgress progress)
        { Configure(config,font); Progress=progress; embedded=true; }

        void Start()
        {
            if(tuning == null) { Debug.LogError("Missing battle tuning"); enabled = false; return; }
            if(!tuning.Validate(out string error)) { Debug.LogError(error); enabled = false; return; }
            Application.targetFrameRate = 60;
            var ui = new GameObject("BattleCanvas"); ui.transform.SetParent(transform);
            View = ui.AddComponent<BattleView>();
            var art = new Sprite[3];
            foreach(var spirit in tuning.playerDeck) if(art[(int)spirit.element] == null) art[(int)spirit.element] = spirit.portrait;
            View.Initialize(japaneseFont,art,tuning);
            if(quest!=null&&quest.useEnemyPortrait)View.SetEnemyPortrait(quest.enemyPortrait);
            View.Selected += Select;
            View.Restarted += () => { if(embedded)RetryRequested?.Invoke(); else Restart(); };
            View.PausePressed += TogglePause;
            View.CardPressed += BeginHold; View.CardReleased += EndHold;
            Restart();
            if(embedded) { View.EnableHomeButton(Retire,quest!=null); View.HideOverlay(); }
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--smoke-test") >= 0) StartCoroutine(RunSmoke());
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--feedback-smoke-test") >= 0) gameObject.AddComponent<FeedbackSmoke>().Begin(this,tuning);
        }
        public void Restart()
        {
            if(Simulation != null)
            {
                Simulation.CollectedCharge -= OnCollected;
                Simulation.TrapTriggered -= OnTrapTriggered;
                Simulation.SpecialTriggered -= OnSpecialTriggered;
                Simulation.AllCleared -= OnAllCleared;
            }
            Simulation = new ShotSimulation(tuning); Simulation.CollectedCharge += OnCollected;
            Simulation.TrapTriggered += OnTrapTriggered;
            Simulation.SpecialTriggered += OnSpecialTriggered;
            Simulation.AllCleared += OnAllCleared;
            State = new BattleState(tuning,quest!=null&&quest.IsPractice); Deck = new SpiritDeck(tuning.seed); EnemyDeck = new SpiritDeck(tuning.seed+1);
            enemyRandom = new System.Random(tuning.seed+2);
            SelectedSlot = 0; dragging = paused = showChain = enemyActing = rewardGranted = false;
            EndHold(); View.HideAim(); View.Feedback.ResetFeedback(); LastAction=default; PrepareEnemyCard();
            actingSpirit = PlayerCard; Phase = BattlePhase.Ready; phaseTime = 0;
            View.AnimateAction(Phase,0,actingSpirit,Simulation.Position,false);
            View.SetStatus("手札長押しでスキル詳細 / ボールから上へスワイプ"); Refresh();
        }
        void PrepareEnemyCard()
        {
            enemySlot = enemyRandom.Next(SpiritDeck.HandSize);
            State.Enemy.Element = EnemyCard.element;
        }
        public void Select(int slot)
        {
            if(View.OverlayVisible || paused || Phase != BattlePhase.Ready || slot < 0 || slot >= SpiritDeck.HandSize) return;
            SelectedSlot = slot; actingSpirit = PlayerCard; dragging = false; View.HideAim(); Refresh();
        }
        public bool Shoot(Vector2 direction)
        {
            if(View.OverlayVisible || paused || Phase != BattlePhase.Ready || View.DetailsVisible) return false;
            actingSpirit = PlayerCard;
            if(!Simulation.Launch(direction,actingSpirit,State.Player.SpeedMultiplier)) return false;
            State.Player.Element = actingSpirit.element; enemyActing = false; showChain = true;
            View.Feedback.BeginShot(false,actingSpirit.element);
            View.SetStatus("全接触数で通常攻撃 / 同属性数でスキル強化");
            Enter(BattlePhase.PlayerShot); return true;
        }
        public void TogglePause()
        {
            if(View.OverlayVisible || Phase == BattlePhase.Finished) return;
            paused = !paused; dragging = false; EndHold(); View.HideAim();
            View.Feedback.SetPaused(paused);
            View.SetStatus(paused ? "一時停止中：一時停止ボタンでもどる" : "再開しました"); Refresh();
        }
        void BeginHold(int slot)
        {
            if(Phase != BattlePhase.Ready || paused || View.OverlayVisible) return;
            heldSlot = slot; heldTime = 0;
        }
        void EndHold() { heldSlot = -1; heldTime = 0; if(View != null) View.HideDetails(); }
        void OnApplicationFocus(bool focus)
        { if(!focus) { dragging = false; EndHold(); if(View != null) View.HideAim(); } }
        void Update()
        {
            if(View == null || View.OverlayVisible || paused || Phase == BattlePhase.Finished) return;
            float dt = Mathf.Min(Time.deltaTime,.05f);
            if(heldSlot >= 0)
            {
                heldTime += dt;
                if(heldTime >= tuning.longPressSeconds) View.ShowDetails(tuning.playerDeck[Deck.CardAt(heldSlot)]);
            }
            if(Phase == BattlePhase.Ready) { ReadPointer(); return; }
            if(Phase == BattlePhase.PlayerShot || Phase == BattlePhase.EnemyShot)
            {
                int resetsBefore=Simulation.ResetCount;
                Simulation.Tick(dt);
                if(Simulation.ResetCount!=resetsBefore)Refresh();
                View.UpdateSimulationVisuals(Simulation,actingSpirit);
                if(State.Finished)
                {
                    Simulation.CancelShot();
                    Finish();
                    return;
                }
                if(!Simulation.Moving)
                {
                    View.Feedback.LockTotal(CurrentNormalDamage());
                    Enter(BattlePhase.Charging); View.SetStatus("接触ダメージ確定 → 通常攻撃 → スキル");
                }
                return;
            }
            phaseTime += dt;
            float duration = Duration(Phase);
            View.AnimateAction(Phase,Mathf.Clamp01(phaseTime/duration),actingSpirit,Simulation.Position,enemyActing);
            if(phaseTime < duration) return;
            switch(Phase)
            {
                case BattlePhase.Charging:
                    Enter(BattlePhase.DamageFlight);View.SetStatus("ためた力を、相手へ！");break;
                case BattlePhase.DamageFlight:
                    LastAction=State.ResolveNormal(enemyActing,actingSpirit,Simulation.Collected,Simulation.Matched,tuning,Simulation.WeightedContacts);
                    View.ShowNormalImpact(LastAction); View.SetStatus("接触でためたダメージを解放！");
                    Enter(BattlePhase.NormalImpact); break;
                case BattlePhase.NormalImpact: Enter(BattlePhase.CutIn); break;
                case BattlePhase.CutIn:
                    LastAction=State.ResolveSkill();
                    View.ShowSkillImpact(LastAction,actingSpirit); View.SetStatus((enemyActing ? "敵：" : "味方：") + LastAction.Summary);
                    Enter(BattlePhase.Impact); break;
                case BattlePhase.Impact: CompleteAction(); break;
                case BattlePhase.EnemyThinking: StartEnemyShot(); break;
            }
        }
        float Duration(BattlePhase phase)
        {
            if(phase == BattlePhase.Charging) return tuning.chargeSeconds;
            if(phase == BattlePhase.DamageFlight) return Mathf.Max(.05f,tuning.damageFlightSeconds);
            if(phase == BattlePhase.NormalImpact) return Mathf.Max(.05f,tuning.normalImpactSeconds);
            if(phase == BattlePhase.CutIn) return tuning.cutInSeconds;
            if(phase == BattlePhase.Impact) return tuning.impactSeconds;
            return Mathf.Max(.01f,tuning.enemyThinkSeconds);
        }
        void Enter(BattlePhase phase)
        {
            Phase = phase; phaseTime = 0;
            View.AnimateAction(phase,0,actingSpirit,Simulation.Position,enemyActing); Refresh();
        }
        void StartEnemyShot()
        {
            enemyActing = true; actingSpirit = EnemyCard;
            float sideInset=MasterConstants.GetFloat("PrototypeBattle.enemyLaunchSideInset",50);
            Simulation.Prepare(true,Mathf.Lerp(sideInset,ShotSimulation.Width-sideInset,(float)enemyRandom.NextDouble()));
            float angle = Mathf.Lerp(tuning.enemyMinimumAngle*Mathf.PI,(1-tuning.enemyMinimumAngle)*Mathf.PI,(float)enemyRandom.NextDouble());
            if(!Simulation.Launch(new Vector2(Mathf.Cos(angle),-Mathf.Sin(angle))*300,actingSpirit,State.Enemy.SpeedMultiplier)) throw new System.InvalidOperationException("Enemy launch failed");
            View.Feedback.BeginShot(true,actingSpirit.element);
            showChain = true; View.SetStatus("敵がショット！相手も接触数と同属性で攻撃");
            Enter(BattlePhase.EnemyShot);
        }
        void CompleteAction()
        {
            // Refill only the slots cleared during this action.
            Simulation.ResetBoard();
            if(enemyActing) EnemyDeck.Replace(enemySlot); else Deck.Replace(SelectedSlot);
            if(State.Finished) { Finish(); return; }
            if(State.Practice && State.Enemy.Hp<=0)
            {
                State.ContinuePractice();enemyActing=showChain=false;actingSpirit=PlayerCard;
                Simulation.Prepare(false,ShotSimulation.Width/2);
                View.SetStatus("目標達成！練習を続けるか、リタイアでクリアできます。");Enter(BattlePhase.Ready);return;
            }
            showChain = false;
            if(!enemyActing)
            {
                actingSpirit = EnemyCard; Simulation.Prepare(true,ShotSimulation.Width/2);
                View.SetStatus("盤面補充 / 敵のターン"); Enter(BattlePhase.EnemyThinking);
            }
            else
            {
                enemyActing = false; PrepareEnemyCard(); actingSpirit = PlayerCard;
                Simulation.Prepare(false,ShotSimulation.Width/2);
                View.SetStatus("盤面補充 / あなたのターン"); Enter(BattlePhase.Ready);
            }
        }
        void Finish()
        {
            if(rewardGranted)return;
            int xp = State.Won || tuning.rewardOnDefeat ? tuning.rewardExperience : 0;
            int coins = State.Won || tuning.rewardOnDefeat ? tuning.rewardCoins : 0;
            if(quest!=null)
            {
                var receipt=completeQuest?.Invoke(State.Won);
                rewardGranted=true;paused=false;dragging=false;Simulation.CancelShot();
                View.Feedback.SetPaused(false);Enter(BattlePhase.Finished);
                View.ShowOverlay(State.Won?"QUEST CLEAR!":"もう一度、挑戦！",receipt?.Summary??"このクエストは終了しています。","もう一度挑戦");
                return;
            }
            if(!rewardGranted)
            {
                rewardGranted = true;
                Progress.Award(xp,coins,tuning);
                ExperienceEarned?.Invoke(xp);
            }
            Enter(BattlePhase.Finished);
            View.ShowOverlay(State.Won ? "VICTORY!" : "もう一度、挑戦！",
                $"バトル終了\n\n報酬 EXP +{xp} / Coin +{coins}\nRank {Progress.Rank}  累計EXP {Progress.TotalExperience}\n参加スピリターにも経験値を獲得\n\n同属性でスキル、全接触で通常攻撃\n手札と反射角を変えて再挑戦！\n※報酬はアプリ終了まで保持","もう一度バトル");
        }
        public void Retire()
        {
            if(Phase!=BattlePhase.Finished && State.Practice && State.Won){Finish();return;}
            HomeRequested?.Invoke();
        }
        void ReadPointer()
        {
            var pointer = Pointer.current; if(pointer == null || View.DetailsVisible) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(View.Board,pointer.position.ReadValue(),null,out Vector2 point);
            if(pointer.press.wasPressedThisFrame)
            {
                if(Vector2.Distance(point,Simulation.Position) <= tuning.shotRadius+tuning.touchPadding) dragging = true;
                else if(point.x >= 0 && point.x <= ShotSimulation.Width && Mathf.Abs(point.y-ShotSimulation.Height*tuning.launchInset) <= MasterConstants.GetFloat("PrototypeBattle.launchLineTouchHalfHeight",42))
                { Simulation.SetLaunchX(point.x); Refresh(); }
            }
            if(dragging) View.SetAim(Simulation.Position,point-Simulation.Position);
            if(pointer.press.wasReleasedThisFrame && dragging)
            {
                dragging = false; View.HideAim();
                if(!Shoot(point-Simulation.Position)) View.SetStatus("ボールから上方向へ、少し長めにスワイプしてください");
            }
        }
        int CurrentNormalDamage() => BattleRules.ContactDamage(
            actingSpirit.Attack * (enemyActing ? State.Enemy.AttackMultiplier : State.Player.AttackMultiplier),
            Simulation.WeightedContacts,actingSpirit.element,
            enemyActing?State.Player.Element:State.Enemy.Element,tuning);
        void OnSpecialTriggered(int index,SpecialCharge effect)
        {
            string[] names = { "", "攻撃力アップ", "速度アップ", "分裂", "縦ミサイル", "横ミサイル", "ワープ" };
            View.SetStatus(names[(int)effect] + "！");
        }
        void OnAllCleared()
        {
            View.Feedback.ShowAllClear();
            View.SetStatus("全消し！ ショットが続けば5秒後にチャージボールを再配置");
        }
        void OnTrapTriggered(bool enemyOwner,int rawDamage)
        {
            var target = enemyOwner ? State.Player : State.Enemy;
            int damage = target.ReceiveTrap(rawDamage);
            View.SetStatus($"罠が発動！ {(enemyOwner ? "味方" : "敵")}に {damage} ダメージ");
            if(State.Finished) Simulation.CancelShot();
            Refresh();
        }
        void OnCollected(int index,bool same)
        {
            var charge=Simulation.Charges[index];
            View.Feedback.Contact(charge.Position,charge.Element,same,Simulation.Collected,Simulation.Matched,CurrentNormalDamage());
            Refresh();
        }
        void Refresh() => View.Refresh(Simulation,Deck,State,tuning,SelectedSlot,showChain,actingSpirit,EnemyCard,Phase,paused,Progress);
        void OnDestroy() { if(Simulation != null)
            {
                Simulation.CollectedCharge -= OnCollected;
                Simulation.TrapTriggered -= OnTrapTriggered;
                Simulation.SpecialTriggered -= OnSpecialTriggered;
                Simulation.AllCleared -= OnAllCleared;
            } }

        IEnumerator RunSmoke()
        {
            yield return new WaitForSeconds(1);
            View.HideOverlay(); Restart();
            string directory = System.IO.Path.Combine(Application.dataPath,"..","Smoke");
            System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory,"ready-v2.png"));
            BeginHold(0); yield return new WaitForSeconds(tuning.longPressSeconds+.2f);
            if(!View.DetailsVisible) { Debug.LogError("SMOKE: long hold failed"); Application.Quit(2); yield break; }
            EndHold();
            if(!Shoot(new Vector2(100,450))) { Debug.LogError("SMOKE: launch failed"); Application.Quit(3); yield break; }
            TogglePause(); Vector2 pausedPosition = Simulation.Position;
            yield return new WaitForSeconds(.3f);
            if(Simulation.Position != pausedPosition) { Debug.LogError("SMOKE: pause failed"); Application.Quit(4); yield break; }
            TogglePause();
            bool sawCharge=false,sawCutIn=false,sawEnemy=false;
            float timeout=Time.realtimeSinceStartup+35;
            while(State.Turn == 1 && !State.Finished && Time.realtimeSinceStartup < timeout)
            {
                if(Phase == BattlePhase.Charging) sawCharge=true;
                if(Phase == BattlePhase.CutIn && !sawCutIn) { sawCutIn=true; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory,"cutin-v2.png")); }
                if(Phase == BattlePhase.EnemyShot) sawEnemy=true;
                yield return null;
            }
            while(Resolving && Time.realtimeSinceStartup < timeout) yield return null;
            if(State.Turn != 2 || Simulation.ResetCount < 3 || !sawCharge || !sawCutIn || !sawEnemy)
            { Debug.LogError($"SMOKE: cycle failed turn={State.Turn} resets={Simulation.ResetCount}"); Application.Quit(5); yield break; }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory,"cycle-v2.png"));
            Restart();
            if(State.Turn != 1 || State.Player.Hp != BattleRules.TotalHp(tuning.playerDeck)) { Debug.LogError("SMOKE: retry failed"); Application.Quit(6); yield break; }
            Debug.Log("SMOKE V2 PASS: hold details, pause, player shot, charge, cut-in, enemy shot, two board refills, next turn, retry.");
            yield return new WaitForSeconds(.5f);
            Application.Quit(0);
        }
    }
}
