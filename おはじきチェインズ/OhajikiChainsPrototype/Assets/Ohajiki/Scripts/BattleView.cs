using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Ohajiki
{
    public sealed class BattleView : MonoBehaviour
    {
        public event Action<int> Selected;
        public event Action Restarted;
        public event Action PausePressed;
        public event Action<int> CardPressed;
        public event Action CardReleased;
        public RectTransform Board { get; private set; }
        public BattleFeedback Feedback { get; private set; }
        public Vector2 ScreenPosition => root.anchoredPosition;
        bool supportImpact;
        public bool OverlayVisible => overlay.gameObject.activeSelf;
        readonly Color ink = SpiritUiSkin.Ink;
        readonly Color muted = SpiritUiSkin.Muted;
        readonly Color panel = SpiritUiSkin.Panel;
        readonly Color[] colors = { new Color(1, .3f, .08f), new Color(.1f, .65f, 1), new Color(.3f, .82f, .2f), new Color(.73f, .37f, 1) };
        SpiritUiSkin skin;
        BattleTuning tuning;
        TMP_Text enemyName, enemyIconLabel, enemyStatusText, progressText, launchHint, detailText, cutInText, impactText;
        UnityEngine.UI.Image enemyIconImage, cutInArt;
        RectTransform detailPanel, energy, cutInPanel;
        UnityEngine.UI.Button[] cardButtons;
        public bool DetailsVisible => detailPanel.gameObject.activeSelf;
        readonly string[] elements = { "火", "水", "木", "無" };
        readonly string[] skills = { "固定攻撃", "固定回復", "固定防御", "割合攻撃", "割合回復", "割合防御", "攻撃アップ", "速度アップ", "悪効果解除", "攻撃ダウン", "速度ダウン" };
        RectTransform root, overlay, projectile, aim;
        RectTransform[] charges;
        UnityEngine.UI.Image[] chargeImages, cardImages;
        TMP_Text[] chargeLabels, cardLabels;
        UnityEngine.UI.Image playerFill, enemyFill;
        TMP_Text playerText, enemyText, turnText, statusText, chainText, deckText, overlayTitle, overlayBody, overlayButtonText;
        RectTransform chainPanel;
        Sprite circle;
        TMP_FontAsset font;
        Sprite[] portraits;
        UnityEngine.UI.Image[] cardPortraits;
        UnityEngine.UI.Image[] cardMarks;
        SpiritBallVisual shotArt,enemyBallArt;
        bool useEnemyPortrait;
        Sprite enemyPortrait;
        public void SetEnemyPortrait(Sprite sprite) {useEnemyPortrait=true;enemyPortrait=sprite;}
        RectTransform impactPanel;
        TMP_Text impactCaption,impactUnit;
        UnityEngine.UI.Image targetFlash;
        float chainSlide;
        bool chainVisible;
        readonly List<RectTransform> cloneVisuals = new List<RectTransform>();
        readonly List<SpiritBallVisual> cloneArt = new List<SpiritBallVisual>();
        readonly List<RectTransform> trapVisuals = new List<RectTransform>();
        readonly List<TMP_Text> trapLabels = new List<TMP_Text>();
        readonly string[] specialLabels = { "", "攻", "速", "分", "縦", "横", "転" };
        Canvas canvas;
        int lastWidth, lastHeight;

        public void Initialize(TMP_FontAsset japaneseFont, Sprite[] characterPortraits, BattleTuning config)
        {
            font = japaneseFont; portraits = characterPortraits; tuning = config;
            skin=gameObject.AddComponent<SpiritUiSkin>();skin.Initialize();circle=skin.Ball;
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform);
            root = Rect("Screen", transform, 0, 0, 720, 1280);
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f); root.pivot = new Vector2(.5f, .5f); root.anchoredPosition = Vector2.zero;
            Image(root, new Color(.55f,.86f,1));skin.Backdrop(root,true);
            Panel(root,"BattleHeader",24,1162,672,103,new Color(1,.97f,.84f,.94f));
            progressText = Label(root, "", 32, 1226, 520, 30, 18, muted);
            Label(root, "<color=#ED5518>おはじき</color><color=#127890>チェインズ</color>", 32, 1171, 520, 60, 31, ink);
            Button(root, "再挑戦", 574, 1218, 114, 42, () => Restarted?.Invoke());
            Button(root, "一時停止", 574, 1168, 114, 42, () => PausePressed?.Invoke());
            var enemy = Rect("Enemy", root, 36, 1088, 656, 70);
            Image(enemy, new Color(.06f,.18f,.22f));
            Image(Rect("Enemy panel fill", enemy, 3, 3, 650, 64), new Color(.94f,.90f,.58f));
            var enemyIcon = Panel(enemy, "EnemyIcon", 44, 6, 60, 60, colors[2], true);
            enemyIconImage = enemyIcon.GetComponent<UnityEngine.UI.Image>();
            enemyIconLabel = Label(enemyIcon, "", 0, 0, 60, 60, 28, Color.white, TextAlignmentOptions.Center);skin.Text(enemyIconLabel,true);
            enemyIconLabel.gameObject.SetActive(false);
            enemyBallArt=enemyIcon.gameObject.AddComponent<SpiritBallVisual>();enemyBallArt.Initialize(skin);
            enemyName = Label(enemy, "", 128, 14, 220, 44, 28, ink);
            enemyName.textWrappingMode = TextWrappingModes.NoWrap;
            enemyName.enableAutoSizing=true; enemyName.fontSizeMin=18; enemyName.fontSizeMax=28;
            Image(Rect("Enemy HP area", enemy, 360, 4, 284, 62), new Color(.98f,.94f,.76f));
            enemyText = Label(enemy, "", 374, 35, 250, 30, 20, muted);
            enemyText.textWrappingMode = TextWrappingModes.NoWrap;
            enemyText.enableAutoSizing = true;
            enemyText.fontSizeMin = 12;
            enemyText.fontSizeMax = 20;
            enemyStatusText = Label(enemy, "", 374, 20, 246, 15, 10, muted);
            enemyStatusText.textWrappingMode = TextWrappingModes.NoWrap;
            enemyStatusText.enableAutoSizing = true;
            enemyStatusText.fontSizeMin = 8;
            enemyStatusText.fontSizeMax = 10;
            Image(Rect("EnemyBar", enemy, 374, 7, 246, 12), new Color(.45f,.32f,.15f));
            enemyFill = Image(Rect("EnemyFill", enemy, 374, 7, 246, 12), colors[2]);
            turnText = Label(root, "", 36, 969, 300, 40, 20, muted);
            deckText = Label(root, "", 330, 975, 350, 28, 18, muted, TextAlignmentOptions.Right);
            Panel(root, "BoardFrame", 38, 308, 644, 670, new Color(1,.75f,.32f));
            Board = Panel(root, "Board", 48, 318, ShotSimulation.Width, ShotSimulation.Height, Color.white);
            Board.GetComponent<UnityEngine.UI.Image>().sprite=Resources.Load<Sprite>("UI/SandstoneBoard");
            Board.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            Panel(Board, "LaunchZone", 12, 24, 600, 82, new Color(.18f,.7f,.65f,.18f));
            launchHint = Label(Board, "タップで位置調整 → ボールから上へスワイプ", 20, 8, 584, 26, 16, muted, TextAlignmentOptions.Center);
            Panel(Board, "EnemyLaunchZone", 12, 548, 600, 82, new Color(.95f,.4f,.15f,.13f));
            Label(Board, "ENEMY", 420, 595, 172, 30, 16, muted, TextAlignmentOptions.Right);
            int chargeCount = tuning.boardColumns * tuning.boardRows;
            float chargeDiameter=tuning.chargeRadius*2,chargeScale=chargeDiameter/54f;
            charges = new RectTransform[chargeCount]; chargeImages = new UnityEngine.UI.Image[chargeCount]; chargeLabels = new TMP_Text[chargeCount];
            for (int i = 0; i < chargeCount; i++)
            {
                charges[i] = Panel(Board, "Charge_" + i, 0, 0, chargeDiameter, chargeDiameter, Color.white, true);
                chargeImages[i] = charges[i].GetComponent<UnityEngine.UI.Image>();
                skin.Image(charges[i],"Orb rim",skin.Ring,tuning.chargeRadius,tuning.chargeRadius,56*chargeScale,56*chargeScale,new Color(.36f,.15f,.035f,.55f),true);
                skin.Image(charges[i],"Orb shine",skin.Star,15*chargeScale,40*chargeScale,14*chargeScale,14*chargeScale,new Color(1,1,1,.75f),true);
                chargeLabels[i] = Label(charges[i], "", 0, 0, chargeDiameter, chargeDiameter, 22, Color.white, TextAlignmentOptions.Center);skin.Text(chargeLabels[i],true);
            }
            aim = Panel(Board, "Direction", 0, 0, 1, 5, new Color(1, .86f, .44f));
            aim.pivot = new Vector2(0, .5f); aim.gameObject.SetActive(false);
            projectile = Panel(Board, "SpiritBall", 0, 0, 40, 40, Color.white, true);
            skin.Image(projectile,"Spirit glow",skin.Glow,20,20,68,68,new Color(1,1,.7f,.6f),true);
            shotArt=projectile.gameObject.AddComponent<SpiritBallVisual>();shotArt.Initialize(skin);
            var chainViewport = Rect("EffectLevelViewport", root, 0, 989, 286, 86);
            chainViewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            chainPanel = Rect("EffectLevel", chainViewport, 6, 0, 280, 86);
            Image(chainPanel, new Color(.06f,.18f,.22f));
            Image(Rect("Skill panel fill", chainPanel, 3, 3, 274, 80), new Color(.99f,.89f,.84f));
            var chainTitle = Label(chainPanel, "ノーマルスキル", 20, 58, 164, 25, 17, Color.black);
            chainTitle.fontStyle = FontStyles.Bold;
            chainText = Label(chainPanel, "", 130, 4, 132, 78, 60, Color.black, TextAlignmentOptions.Right);
            if(TMP_Settings.defaultFontAsset!=null)chainText.font=TMP_Settings.defaultFontAsset;
            chainText.textWrappingMode = TextWrappingModes.NoWrap;
            chainText.enableAutoSizing = true;
            chainText.fontSizeMin = 40;
            chainText.fontSizeMax = 60;
            Panel(root,"PartyStatus",24,246,672,62,new Color(1,.98f,.86f,.92f));
            playerText = Label(root, "", 36, 274, 648, 30, 20, ink);
            playerText.enableAutoSizing = true;
            playerText.fontSizeMin = 14;
            playerText.fontSizeMax = 20;
            Panel(root, "PlayerBar", 36, 254, 648, 10, new Color(.45f,.32f,.15f));
            playerFill = Image(Rect("PlayerFill", root, 36, 254, 648, 10), new Color(.08f,.68f,.44f));
            cardButtons = new UnityEngine.UI.Button[4];
            cardImages = new UnityEngine.UI.Image[4]; cardLabels = new TMP_Text[4]; cardPortraits = new UnityEngine.UI.Image[4];
            cardMarks=new UnityEngine.UI.Image[4];
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                var card = Button(root, "", 36 + i * 164, 83, 156, 158, () => Selected?.Invoke(slot));
                cardImages[i] = card.GetComponent<UnityEngine.UI.Image>();
                cardButtons[i] = card.GetComponent<UnityEngine.UI.Button>();
                var press = card.gameObject.AddComponent<HandCardPress>();
                press.Pressed = () => CardPressed?.Invoke(slot); press.Released = () => CardReleased?.Invoke();
                var art = Rect("Portrait", card, 27, 52, 102, 104);
                cardPortraits[i] = Image(art, Color.white); cardPortraits[i].preserveAspect = true;
                cardLabels[i] = Label(card, "", 3, 0, 150, 58, 17, ink, TextAlignmentOptions.Center);
                cardMarks[i]=skin.Image(card,"Selected spirit",skin.Star,134,140,24,24,new Color(1,.45f,.02f),true);
            }
            statusText = Label(root, "", 34, 31, 652, 45, 18, muted, TextAlignmentOptions.Center);
            Label(root, "PROTOTYPE  /  仮ルール・数値で戦闘を検証", 34, 5, 652, 22, 12, muted, TextAlignmentOptions.Center);
            Feedback=Rect("BattleFeedback",root,0,0,720,1280).gameObject.AddComponent<BattleFeedback>();
            Feedback.Initialize(Board,font,skin,tuning);
            CreateEffectsAndDetails();
            overlay = Panel(root, "Overlay", 0, 0, 720, 1280, SpiritUiSkin.Paper);
            overlay.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            skin.Backdrop(overlay,false);Panel(overlay,"Result parchment",38,90,644,1110,new Color(1,.97f,.84f,.94f));
            Label(overlay, "OHAJIKI / CHAINS", 50, 1110, 620, 40, 24, new Color(.44f, .83f, .77f), TextAlignmentOptions.Center);
            overlayTitle = Label(overlay, "おはじきチェインズ", 40, 1010, 640, 80, 43, ink, TextAlignmentOptions.Center);
            for (int i = 0; i < 3; i++)
            {
                var portrait = Image(Rect("Hero", overlay, 50 + i * 210, 688 + (i % 2) * 25, 200, 270), Color.white);
                portrait.sprite = portraits[i]; portrait.enabled=portraits[i]!=null; portrait.preserveAspect = true;
            }
            overlayBody = Label(overlay, "", 58, 314, 604, 366, 25, ink, TextAlignmentOptions.Center);
            overlayBody.enableAutoSizing = true;
            overlayBody.fontSizeMin = 22;
            overlayBody.fontSizeMax = 25;
            var start = Button(overlay, "", 160, 226, 400, 76, () => { overlay.gameObject.SetActive(false); Restarted?.Invoke(); });
            start.GetComponent<UnityEngine.UI.Image>().color = SpiritUiSkin.Accent;
            overlayButtonText = Label(start, "", 0, 0, 400, 76, 26, Color.white, TextAlignmentOptions.Center);
            Label(overlay, $"火 → 木 → 水 → 火　有利 ×{tuning.advantage} / 不利 ×{tuning.disadvantage}\n無属性はチャージのみ / 手札長押しで詳細", 60, 110, 600, 85, 20, muted, TextAlignmentOptions.Center);
            ShowOverlay("おはじきチェインズ", "選ぶ。はじく。つなげる。\n\n① 下の4枚からスピリターを選ぶ\n② 発射位置をタップで調整\n③ ボールから飛ばす方向へスワイプ\n\n敵もショット！双方の攻撃後に消えたボールを補充", "バトルをはじめる");
            Resize();
        }
        public void EnableHomeButton(Action action,bool quest=false)
        {
            progressText.rectTransform.sizeDelta=new Vector2(400,30);
            var homeButton=Button(root,quest?"リタイア":"ホーム",456,1218,108,42,action);
            homeButton.SetSiblingIndex(overlay.GetSiblingIndex());
            Button(overlay,quest?"クエストへ":"ホームへ",160,35,400,60,action);
        }
        void Update()
        {
            Resize();
            if (chainPanel == null) return;
            chainSlide = Mathf.MoveTowards(chainSlide, chainVisible ? 6 : -286, Time.unscaledDeltaTime * tuning.levelSlideSpeed);
            chainPanel.anchoredPosition = new Vector2(chainSlide, 0);
        }
        void Resize()
        {
            if (lastWidth == Screen.width && lastHeight == Screen.height) return;
            lastWidth = Screen.width; lastHeight = Screen.height;
            canvas.scaleFactor = Mathf.Min(Screen.width / 720f, Screen.height / 1280f);
        }
        public void Refresh(ShotSimulation sim, SpiritDeck deck, BattleState state, BattleTuning config, int selected,
            bool showChain, SpiritDefinition active, SpiritDefinition enemy, BattlePhase phase, bool paused, PlayerProgress progress)
        {
            // Keep the accumulated amount through resolution; no idle or result counter.
            bool feedbackVisible=phase==BattlePhase.PlayerShot || phase==BattlePhase.EnemyShot ||
                phase==BattlePhase.Charging || phase==BattlePhase.DamageFlight || phase==BattlePhase.NormalImpact ||
                phase==BattlePhase.CutIn || phase==BattlePhase.Impact;
            Feedback.SetDamageCounterVisible(feedbackVisible);
            // Avoid showing fragments of these secondary labels behind the shot feedback.
            turnText.gameObject.SetActive(!feedbackVisible);
            deckText.gameObject.SetActive(!feedbackVisible);
            for (int i = 0; i < charges.Length; i++)
            {
                var c = sim.Charges[i]; charges[i].gameObject.SetActive(c.Active);
                charges[i].sizeDelta = Vector2.one * tuning.chargeRadius * 2;
                charges[i].anchoredPosition = c.Position - Vector2.one * tuning.chargeRadius;
                chargeImages[i].color = colors[(int)c.Element];
                bool special = c.Special != SpecialCharge.None;
                chargeLabels[i].fontSize = (special ? 16 : 22)*tuning.chargeRadius/27f;
                chargeLabels[i].text = elements[(int)c.Element] + (special ? "\n" + specialLabels[(int)c.Special] : "");
            }
            projectile.sizeDelta = Vector2.one * tuning.shotRadius * 2;
            UpdateSimulationVisuals(sim,active);
            projectile.GetComponent<UnityEngine.UI.Image>().color = colors[(int)active.element];
            shotArt.Show(active);enemyBallArt.Show(enemy);
            if(useEnemyPortrait)enemyBallArt.SetPortrait(enemyPortrait);
            playerText.text = $"味方 HP {state.Player.Hp}/{state.Player.MaxHp}" + GuardText(state.Player);
            enemyName.text = enemy.displayName;
            enemyIconImage.color = colors[(int)enemy.element]; enemyIconLabel.text = elements[(int)enemy.element];
            enemyText.text = $"HP {state.Enemy.Hp}/{state.Enemy.MaxHp}";
            enemyStatusText.text = GuardText(state.Enemy).TrimStart(' ', '/');
            playerFill.rectTransform.sizeDelta = new Vector2(648f * state.Player.Hp / state.Player.MaxHp,10);
            enemyFill.rectTransform.sizeDelta = new Vector2(246f * state.Enemy.Hp / state.Enemy.MaxHp,12);
            string phaseText = paused ? "一時停止" : PhaseName(phase);
            turnText.text = $"TURN {state.Turn:00} / {phaseText}";
            deckText.text = $"手スピ 4 / 12　山札 {deck.DrawCount}";
            progressText.text = $"Rank {progress.Rank}　EXP {progress.RankExperience}/{progress.Needed(tuning)}　Coin {progress.Coins}";
            for (int i = 0; i < 4; i++)
            {
                var spirit = tuning.playerDeck[deck.CardAt(i)]; int e = (int)spirit.element;
                cardImages[i].color = i == selected ? new Color(1,.83f,.37f) : Color.Lerp(panel,colors[e],.08f);
                cardMarks[i].gameObject.SetActive(i==selected);
                cardPortraits[i].sprite = spirit.portrait;
                cardPortraits[i].enabled=spirit.portrait!=null;
                cardLabels[i].text = $"{elements[e]} {spirit.displayName}\n{skills[(int)spirit.skill]}";
                cardButtons[i].interactable = !paused && phase == BattlePhase.Ready;
            }
            if(showChain && !chainVisible)
            {
                chainSlide = -286;
                chainPanel.anchoredPosition = new Vector2(chainSlide, 0);
            }
            chainVisible = showChain;
            chainPanel.gameObject.SetActive(showChain && (phase==BattlePhase.PlayerShot || phase==BattlePhase.EnemyShot || phase==BattlePhase.Charging));
            chainText.text = $"LV{BattleRules.EffectLevel(sim.Matched,tuning)}";
            launchHint.text = sim.EnemySide ? "敵のショット / 下向きにランダム発射" : "タップで位置調整 → ボールから上へスワイプ";
        }
        static string GuardText(Combatant side)
        {
            string text = side.GuardTurns > 0 ? $" / 防御{side.GuardTurns}回" : "";
            if(!Mathf.Approximately(side.AttackMultiplier,1)) text += $" 攻×{side.AttackMultiplier:0.##}";
            if(!Mathf.Approximately(side.SpeedMultiplier,1)) text += $" 速×{side.SpeedMultiplier:0.##}";
            return text;
        }
        public void UpdateSimulationVisuals(ShotSimulation simulation,SpiritDefinition spirit)
        {
            MoveProjectile(simulation.Position,tuning.shotRadius);
            int cloneCount = Mathf.Max(0,simulation.Projectiles.Count - 1);
            while(cloneVisuals.Count < cloneCount)
            {
                var visual = Panel(Board,"SplitSpirit",0,0,tuning.shotRadius*2,tuning.shotRadius*2,Color.white,true);
                visual.gameObject.AddComponent<UnityEngine.CanvasGroup>().alpha = .5f;
                var art = visual.gameObject.AddComponent<SpiritBallVisual>();
                art.Initialize(skin);
                cloneVisuals.Add(visual);
                cloneArt.Add(art);
            }
            for(int i=0;i<cloneVisuals.Count;i++)
            {
                bool visible = i<cloneCount && simulation.Projectiles[i+1].Active;
                cloneVisuals[i].gameObject.SetActive(visible);
                if(!visible) continue;
                cloneVisuals[i].anchoredPosition = simulation.Projectiles[i+1].Position - Vector2.one*tuning.shotRadius;
                cloneArt[i].Show(spirit);
            }
            while(trapVisuals.Count < simulation.Traps.Count)
            {
                var visual = Panel(Board,"Trap",0,0,56,56,new Color(.4f,.15f,.6f,.8f),true);
                trapLabels.Add(Label(visual,"",0,0,56,56,14,Color.white,TextAlignmentOptions.Center));
                trapVisuals.Add(visual);
            }
            for(int i=0;i<trapVisuals.Count;i++)
            {
                bool visible = i<simulation.Traps.Count;
                trapVisuals[i].gameObject.SetActive(visible);
                if(!visible) continue;
                var trap = simulation.Traps[i];
                trapVisuals[i].anchoredPosition = trap.Position - Vector2.one*28;
                trapLabels[i].text = (trap.EnemyOwner ? "敵罠" : "味方罠") + "\n" + elements[(int)trap.TargetElement];
            }
        }
        public static string PhaseName(BattlePhase phase)
        {
            switch(phase) {
                case BattlePhase.Ready: return "あなたの番";
                case BattlePhase.PlayerShot: return "味方ショット";
                case BattlePhase.EnemyThinking: return "敵の準備";
                case BattlePhase.EnemyShot: return "敵ショット";
                case BattlePhase.Charging: return "ダメージ確定";
                case BattlePhase.DamageFlight: return "ダメージ解放";
                case BattlePhase.NormalImpact: return "通常攻撃";
                case BattlePhase.CutIn: return "スキル準備";
                case BattlePhase.Impact: return "スキル発動";
                default: return "終了";
            }
        }
        public void MoveProjectile(Vector2 position, float radius)
        {projectile.anchoredPosition = position - Vector2.one * radius;Feedback?.MoveShot(position);}
        public void SetStatus(string text) => statusText.text = text;
        public void SetAim(Vector2 origin, Vector2 delta)
        {
            aim.gameObject.SetActive(delta.magnitude >= tuning.minimumSwipe && delta.y >= tuning.minimumForwardSwipe);
            aim.anchoredPosition = origin;
            aim.sizeDelta = new Vector2(Mathf.Min(delta.magnitude, 180), 5);
            aim.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
        public void HideAim() => aim.gameObject.SetActive(false);
        public void ShowOverlay(string title, string body, string button)
        {
            overlayTitle.text = title; overlayBody.text = body; overlayButtonText.text = button;
            overlay.gameObject.SetActive(true);
        }
        void CreateEffectsAndDetails()
        {
            energy = Panel(Board, "ChargeEnergy",0,0,100,100,new Color(1,.8f,.4f,.4f),true);
            energy.GetComponent<UnityEngine.UI.Image>().sprite=skin.Glow;
            energy.pivot = new Vector2(.5f,.5f); energy.gameObject.SetActive(false);
            cutInPanel = Panel(root,"SkillCutIn",740,520,640,300,new Color(1,.95f,.76f,.97f));
            skin.Halo(cutInPanel,new Vector2(466,155),325,new Color(.13f,.65f,.7f,.55f));
            for(int i=0;i<8;i++)skin.Image(cutInPanel,"Skill streak",skin.Shard,45+i*80,145,12,275,new Color(1,.7f,.15f,.18f),true).rectTransform.localRotation=Quaternion.Euler(0,0,-22);
            cutInArt = Image(Rect("Art",cutInPanel,330,0,270,330),Color.white); cutInArt.preserveAspect = true;
            cutInText = Label(cutInPanel,"",28,50,320,210,30,ink,TextAlignmentOptions.Center);
            cutInPanel.gameObject.SetActive(false);
            targetFlash=skin.Image(root,"Target damage flash",skin.Burst,530,1115,370,200,Color.clear,true);
            impactPanel=Panel(root,"Impact readout",80,789,560,190,new Color(.12f,.18f,.21f,.96f));
            impactPanel.pivot=new Vector2(.5f,.5f);
            skin.Image(impactPanel,"Impact accent",skin.Shard,18,95,16,145,SpiritUiSkin.Gold,true);
            skin.Image(impactPanel,"Impact accent",skin.Shard,542,95,16,145,SpiritUiSkin.Gold,true);
            impactCaption=Label(impactPanel,"",20,135,520,50,25,Color.white,TextAlignmentOptions.Center);
            impactText = Label(impactPanel,"",20,38,520,115,85,new Color(1,.91f,.4f),TextAlignmentOptions.Center);
            impactText.enableAutoSizing=true;impactText.fontSizeMin=34;impactText.fontSizeMax=85;
            skin.Text(impactText,true);
            impactText.outlineWidth=.18f; impactText.outlineColor=new Color(.25f,.10f,.015f,1);
            impactUnit=Label(impactPanel,"",20,3,520,46,23,SpiritUiSkin.Gold,TextAlignmentOptions.Center);
            impactPanel.gameObject.SetActive(false);
            detailPanel = Panel(root,"SkillDetails",50,330,620,520,SpiritUiSkin.Paper);
            detailText = Label(detailPanel,"",30,25,560,470,24,ink,TextAlignmentOptions.Center);
            detailText.enableAutoSizing = true;
            detailText.fontSizeMin = 16;
            detailText.fontSizeMax = 24;
            detailPanel.gameObject.SetActive(false);
        }
        public void ShowDetails(SpiritDefinition spirit)
        {
            string powers = "";
            for(int i=0;i<(spirit.skillPower?.Length??0);i++)
                powers += $"Lv{i+1}: " + (spirit.skill != Skill.Cleanse && (int)spirit.skill >= (int)Skill.PercentAttack ? $"{spirit.skillPower[i]*100:0.#}%" : $"{spirit.skillPower[i]:0}") + "  ";
            detailText.text = $"{elements[(int)spirit.element]} {spirit.displayName}  Lv{spirit.level}\nHP {spirit.Hp} / 攻撃 {spirit.Attack} / 速度 {spirit.Speed:0}\n\n{spirit.skillName} / {skills[(int)spirit.skill]}\n{powers}\n" +
                (spirit.skill == Skill.Guard || spirit.skill == Skill.PercentGuard ? $"防御は相手の行動{spirit.guardTurns}回分\n" : "") +
                ActionDescription(spirit) +
                ((int)spirit.skill >= (int)Skill.AttackUp && spirit.skill != Skill.Cleanse ? $"能力変化は対象の行動{spirit.statusTurns}回分\n" : "") +
                $"\n通常攻撃 = 攻撃 × 接触数（強化込み） × {tuning.contactAttackFactor}\n無属性も通常攻撃の接触数に含む\n\n指・ボタンを離すと閉じます";
            detailPanel.gameObject.SetActive(true);
        }
        string ActionDescription(SpiritDefinition spirit)
        {
            if(spirit.actionSkill == ActionSkill.Trap)
                return $"追加スキル 罠 / 対象 {elements[(int)spirit.trapTargetElement]}\n威力{spirit.trapDamage} / 相手{spirit.trapOpponentTurns}行動まで\n";
            if(spirit.actionSkill == ActionSkill.Split)
                return $"追加スキル 分裂 / 接触{spirit.splitContactThreshold}個で{spirit.splitTotalProjectiles}体\n";
            return "";
        }
        public void HideDetails() => detailPanel.gameObject.SetActive(false);
        public void HideOverlay() => overlay.gameObject.SetActive(false);
        public bool DamageVisualsVisible=>Feedback.DamageVisualsVisible || targetFlash.color.a>0 ||
            (impactPanel.gameObject.activeInHierarchy && impactUnit.text=="DAMAGE");
        public void AnimateAction(BattlePhase phase,float progress,SpiritDefinition spirit,Vector2 position,bool enemy)
        {
            energy.gameObject.SetActive(phase == BattlePhase.Charging);
            cutInPanel.gameObject.SetActive(phase == BattlePhase.CutIn);
            bool impact=phase == BattlePhase.Impact || phase == BattlePhase.NormalImpact;
            impactPanel.gameObject.SetActive(impact);
            if(impact)
            {
                float kick=Mathf.Exp(-progress*8);
                impactPanel.localScale=new Vector3(1+.13f*kick,1+.19f*kick,1);
                targetFlash.color=Color.clear;
                targetFlash.rectTransform.localRotation=Quaternion.Euler(0,0,progress*26);
            }
            else targetFlash.color=Color.clear;
            if(phase==BattlePhase.DamageFlight)Feedback.AnimateDamageFlight(progress,enemy);
            else Feedback.HideFlight();
            if(phase == BattlePhase.Charging)
            {
                energy.anchoredPosition = position;
                energy.sizeDelta = Vector2.one * Mathf.Lerp(tuning.shotRadius*2,tuning.effectMaxDiameter,progress);
                Color color = colors[(int)spirit.element]; color.a = .25f + .25f*Mathf.Sin(progress*Mathf.PI*4);
                energy.GetComponent<UnityEngine.UI.Image>().color = color;
            }
            if(phase == BattlePhase.CutIn)
            {
                float ease = 1-Mathf.Pow(1-Mathf.Clamp01(progress*2),3);
                cutInPanel.anchoredPosition = new Vector2(Mathf.Lerp(740,40,ease),520);
                cutInArt.sprite = spirit.portrait;
                cutInArt.enabled=spirit.portrait!=null;
                cutInArt.rectTransform.localScale=Vector3.one*Mathf.Lerp(1.16f,1,progress);
                cutInText.text = (enemy ? "ENEMY SKILL" : "SPIRIT SKILL") + $"\n{spirit.displayName}\n\n{spirit.skillName}";
            }
            root.anchoredPosition = Vector2.zero;
        }
        public void ShowNormalImpact(ActionResult result)
        {
            supportImpact=false;
            impactCaption.text="通常攻撃";impactUnit.text="DAMAGE";
            impactText.text=$"{result.appliedNormalDamage:N0}";
            impactText.color=new Color(1,.85f,.43f);
            impactPanel.anchoredPosition=new Vector2(360,result.enemy?410:866);
            targetFlash.rectTransform.anchoredPosition=result.enemy?new Vector2(360,278):new Vector2(530,1115);
            Feedback.Impact(false,result.enemy,false);
        }
        public void ShowSkillImpact(ActionResult result,SpiritDefinition spirit)
        {
            supportImpact=spirit.skill!=Skill.Attack && spirit.skill!=Skill.PercentAttack;
            string effect="発動";
            impactUnit.text="SKILL";
            if(spirit.skill==Skill.Heal || spirit.skill==Skill.PercentHeal){effect=$"+{result.healing:N0}";impactUnit.text="HEAL";}
            if(result.guarding){effect="GUARD";impactUnit.text=$"相手の行動 {spirit.guardTurns}回";}
            if(!string.IsNullOrEmpty(result.statusEffect))
            {
                effect = result.statusEffect;
                impactUnit.text = spirit.skill == Skill.Cleanse ? "解除" : $"対象の行動 {spirit.statusTurns}回";
            }
            impactCaption.text=$"{spirit.skillName} / Lv{result.level}";impactText.text=effect;
            impactText.color=supportImpact?new Color(.45f,1,.74f):new Color(1,.85f,.43f);
            bool affectsSelf = supportImpact && spirit.skill != Skill.AttackDown && spirit.skill != Skill.SpeedDown;
            bool bottom=affectsSelf?!result.enemy:result.enemy;
            impactPanel.anchoredPosition=new Vector2(360,bottom?410:866);
            targetFlash.rectTransform.anchoredPosition=bottom?new Vector2(360,278):new Vector2(530,1115);
        }
        RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h); return rt;
        }
        UnityEngine.UI.Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        RectTransform Panel(Transform parent, string name, float x, float y, float w, float h, Color color, bool round = false)
        {
            var rect = Rect(name, parent, x, y, w, h); var image = Image(rect, color);
            if(round)image.sprite=circle;
            else if(w<710 && w>30 && h>20 && name!="Board" && name!="LaunchZone" && name!="EnemyLaunchZone")skin.Frame(image);
            return rect;
        }
        TMP_Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var label = Rect("Label", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = size; label.color = color; label.text = text;
            label.alignment = align; label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.Normal;
            if(size>=26)label.fontStyle=FontStyles.Bold;return label;
        }
        RectTransform Button(Transform parent, string text, float x, float y, float w, float h, Action action)
        {
            var rect = Panel(parent, "Button", x, y, w, h, new Color(1,.89f,.61f));
            rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition+=new Vector2(w*.5f,h*.5f);
            var image = rect.GetComponent<UnityEngine.UI.Image>(); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image; button.onClick.AddListener(() => action());
            rect.gameObject.AddComponent<UiButtonMotion>();
            if (text.Length > 0) Label(rect, text, 0, 0, w, h, 19, ink, TextAlignmentOptions.Center);
            return rect;
        }
    }
}
