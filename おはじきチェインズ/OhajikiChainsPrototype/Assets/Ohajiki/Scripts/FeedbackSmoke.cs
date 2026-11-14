using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
namespace Ohajiki
{
    // Explicit command-line runtime check; never attached in a normal game session.
    public sealed class FeedbackSmoke : MonoBehaviour
    {
        PrototypeBattle battle;
        BattleTuning tuning;
        string output;
        SpiritDefinition[] originalEnemyDeck, durableEnemies;
        int[] originalPlayerHp;
        readonly List<BattlePhase> phases=new List<BattlePhase>();
        public void Begin(PrototypeBattle owner,BattleTuning config){battle=owner;tuning=config;StartCoroutine(Run());}
        void Check(bool condition,string message){if(!condition){Application.Quit(2);throw new InvalidOperationException("FEEDBACK SMOKE: "+message);}}
        void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
        void CheckHpBars()
        {
            var player=(RectTransform)battle.View.transform.Find("Screen/PlayerFill");
            var enemy=(RectTransform)battle.View.transform.Find("Screen/Enemy/EnemyFill");
            Check(player!=null && enemy!=null,"both HP bars exist");
            Check(Mathf.Abs(player.sizeDelta.x-648f*battle.State.Player.Hp/battle.State.Player.MaxHp)<.001f &&
                Mathf.Abs(enemy.sizeDelta.x-246f*battle.State.Enemy.Hp/battle.State.Enemy.MaxHp)<.001f,
                "HP bars match model immediately, without gradual drain");
        }
        void CheckHpBarShake()
        {
            var player=(RectTransform)battle.View.transform.Find("Screen/PlayerBar");
            var enemy=(RectTransform)battle.View.transform.Find("Screen/Enemy/EnemyBar");
            var playerFill=(RectTransform)battle.View.transform.Find("Screen/PlayerFill");
            var enemyFill=(RectTransform)battle.View.transform.Find("Screen/Enemy/EnemyFill");
            var playerHome=player.anchoredPosition;var enemyHome=enemy.anchoredPosition;
            var boardHome=battle.View.Board.anchoredPosition;
            var enemyPanel=(RectTransform)enemy.parent;var panelHome=enemyPanel.anchoredPosition;
            foreach(bool enemyAttack in new[]{false,true})
            {
                var result=new ActionResult{enemy=enemyAttack,appliedNormalDamage=10};
                battle.View.ShowNormalImpact(result);
                battle.View.AnimateAction(BattlePhase.NormalImpact,0,battle.PlayerCard,Vector2.zero,enemyAttack);
                Check((enemyAttack?player:enemy).anchoredPosition!=(enemyAttack?playerHome:enemyHome),"struck HP bar shakes on arrival");
                Check((enemyAttack?enemy:player).anchoredPosition==(enemyAttack?enemyHome:playerHome),"other HP bar remains still");
                Check(playerFill.anchoredPosition==player.anchoredPosition && enemyFill.anchoredPosition==enemy.anchoredPosition,"HP fills and tracks move together");
                Check(battle.View.ScreenPosition==Vector2.zero && battle.View.Board.anchoredPosition==boardHome && enemyPanel.anchoredPosition==panelHome,"only HP bars move, not screen, board or enemy panel");
                battle.View.AnimateAction(BattlePhase.NormalImpact,1,battle.PlayerCard,Vector2.zero,enemyAttack);
                Check(player.anchoredPosition==playerHome && enemy.anchoredPosition==enemyHome,"HP bars return exactly to their starting positions");
                result.appliedNormalDamage=0;battle.View.ShowNormalImpact(result);
                battle.View.AnimateAction(BattlePhase.NormalImpact,0,battle.PlayerCard,Vector2.zero,enemyAttack);
                Check(player.anchoredPosition==playerHome && enemy.anchoredPosition==enemyHome,"zero normal damage does not shake HP bars");
                result.appliedSkillDamage=0;battle.View.ShowSkillImpact(result,battle.PlayerCard);
                battle.View.AnimateAction(BattlePhase.Impact,0,battle.PlayerCard,Vector2.zero,enemyAttack);
                Check(player.anchoredPosition==playerHome && enemy.anchoredPosition==enemyHome,"support or zero skill damage does not shake HP bars");
            }
            battle.View.AnimateAction(BattlePhase.Ready,0,battle.PlayerCard,Vector2.zero,false);
        }
        void CheckImpactReadout(int damage)
        {
            var panel=battle.View.transform.Find("Screen/Impact readout");
            Check(panel!=null && panel.gameObject.activeInHierarchy,"impact readout is visible");
            var texts=panel.GetComponentsInChildren<TMP_Text>();
            Check(texts.Any(text=>text.text=="DAMAGE") && texts.Any(text=>text.text==damage.ToString("N0")),
                "impact readout shows the applied damage exactly");
        }
        void CheckDamageLayout(RectTransform bank)
        {
            Canvas.ForceUpdateCanvases();
            var label=bank.GetComponentInChildren<TMP_Text>();label.ForceMeshUpdate();
            Check(!label.isTextOverflowing && label.textInfo.lineCount==1,"damage digits fit on one line");
            var corners=new Vector3[4];bank.GetWorldCorners(corners);
            foreach(var corner in corners)Check(corner.x>=0 && corner.x<=Screen.width && corner.y>=0 && corner.y<=Screen.height,"damage burst stays on screen at maximum size");
            var enemyPanel=(RectTransform)battle.View.transform.Find("Screen/Enemy");
            var enemyCorners=new Vector3[4];enemyPanel.GetWorldCorners(enemyCorners);
            Check(corners[1].y<enemyCorners[0].y,"growing damage UI stays clear of the enemy HP panel");
        }
        void PrepareDurableCombatants()
        {
            // The presentation test needs both damage stages and a complete enemy turn,
            // even when increased special-ball rates would finish a real battle early.
            // Player cards already belong to the session loadout; enemy assets are shared.
            originalPlayerHp=tuning.playerDeck.Select(s=>s.baseHp).ToArray();
            foreach(var spirit in tuning.playerDeck)spirit.baseHp=50000;
            originalEnemyDeck=tuning.enemyDeck;
            durableEnemies=originalEnemyDeck.Select(s=>Instantiate(s)).ToArray();
            foreach(var spirit in durableEnemies)spirit.baseHp=50000;
            tuning.enemyDeck=durableEnemies;
        }
        void OnDisable()
        {
            // App disables the battle before disposing its loadout. Restore ownership first.
            if(tuning!=null)
            {
                if(originalEnemyDeck!=null)tuning.enemyDeck=originalEnemyDeck;
                if(originalPlayerHp!=null)
                    for(int i=0;i<originalPlayerHp.Length;i++)if(tuning.playerDeck[i]!=null)tuning.playerDeck[i].baseHp=originalPlayerHp[i];
            }
            if(durableEnemies!=null)foreach(var spirit in durableEnemies)if(spirit!=null)Destroy(spirit);
            durableEnemies=originalEnemyDeck=null;originalPlayerHp=null;
        }
        void CheckLevelTextLayout(TMP_Text[] texts)
        {
            Canvas.ForceUpdateCanvases();
            foreach(var text in texts)
            {
                text.ForceMeshUpdate();
                Check(!text.isTextOverflowing && text.textInfo.lineCount==1,
                    "skill level text fits one line: "+text.text);
            }
            var enemyPanel=battle.View.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect=>rect.name=="Enemy");
            Check(enemyPanel!=null,"enemy status panel visible");
            foreach(var text in enemyPanel.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();
                Check(!text.isTextOverflowing && (string.IsNullOrEmpty(text.text) || text.textInfo.lineCount==1),
                    "enemy text fits one line: "+text.text);
            }
        }
        IEnumerator CheckDeferredDefeat(bool enemy)
        {
            battle.View.HideOverlay();battle.Restart();
            battle.PlayerCard.actionSkill=ActionSkill.None;
            battle.PlayerCard.skill=Skill.Attack;battle.PlayerCard.skillPower=new[]{70f};
            foreach(var s in tuning.enemyDeck)s.actionSkill=ActionSkill.None;
            foreach(var c in battle.Simulation.Charges)c.Active=false;
            var target=enemy?battle.State.Player:battle.State.Enemy;
            target.Receive(target.MaxHp-1);
            long coinsBefore=battle.Progress.Coins;
            if(!enemy)PlaceContact(0,Vector2.up);
            Check(battle.Shoot(Vector2.up*200),"deferred defeat fixture launch");
            if(enemy)
            {
                float waitEnemy=Time.realtimeSinceStartup+12;
                while(battle.Phase!=BattlePhase.EnemyShot && Time.realtimeSinceStartup<waitEnemy)yield return null;
                Check(battle.Phase==BattlePhase.EnemyShot,"enemy turn fixture started");
                PlaceContact(0,battle.Simulation.Velocity.normalized);
            }
            float hitDeadline=Time.realtimeSinceStartup+2;
            while(battle.Simulation.Collected==0 && Time.realtimeSinceStartup<hitDeadline)yield return null;
            var shotPhase=enemy?BattlePhase.EnemyShot:BattlePhase.PlayerShot;
            Check(battle.Simulation.Collected>0 && target.Hp==1 && battle.Phase==shotPhase && battle.Simulation.Moving,"lethal accumulated damage leaves HP unchanged during the shot");
            Check(!battle.View.OverlayVisible && battle.State.ShotInProgress && !battle.State.Finished,"no result before damage arrival");
            CheckHpBars();
            string name=enemy?"10-player-zero":"09-enemy-zero";
            Capture(name+"-before-settlement");
            PlaceContact(1,battle.Simulation.Velocity.normalized);
            battle.TogglePause();Vector2 frozen=battle.Simulation.Position;
            yield return new WaitForSeconds(.2f);
            Check(battle.Phase==shotPhase && battle.Simulation.Position==frozen && target.Hp==1 && !battle.View.OverlayVisible,"pause does not apply accumulated damage");
            battle.TogglePause();
            var actual=new List<BattlePhase>();var previous=shotPhase;
            float deadline=Time.realtimeSinceStartup+12;
            while(battle.Phase!=BattlePhase.Finished && Time.realtimeSinceStartup<deadline)
            {
                Check(!battle.View.OverlayVisible && battle.Progress.Coins==coinsBefore,"no scene/result or reward before turn completion");
                Check(battle.Phase!=BattlePhase.Ready && battle.Phase!=BattlePhase.EnemyThinking,"no following side starts after defeat");
                if(battle.Phase==shotPhase || battle.Phase==BattlePhase.Charging || battle.Phase==BattlePhase.DamageFlight)
                    Check(target.Hp==1 && !battle.State.Finished,"HP remains unchanged through shot, zone and flight");
                else Check(target.Hp==0 && battle.State.Won==!enemy,"arrival settlement preserves the first defeat");
                CheckHpBars();
                if(battle.Phase!=previous)
                {
                    previous=battle.Phase;actual.Add(previous);
                    if(previous==BattlePhase.DamageFlight)Capture(name+"-before-arrival");
                    if(previous==BattlePhase.NormalImpact)Capture(name+"-settled-impact");
                    if(previous==BattlePhase.CutIn)Capture(name+"-cutin");
                    if(previous==BattlePhase.Impact)Capture(name+"-skill");
                }
                yield return null;
            }
            Check(actual.SequenceEqual(new[]{BattlePhase.Charging,BattlePhase.DamageFlight,BattlePhase.NormalImpact,BattlePhase.CutIn,BattlePhase.Impact}),"lethal shot completes zone, flight, impact, slide and skill in order");
            Check(battle.Phase==BattlePhase.Finished && battle.View.OverlayVisible,"result opens only after the skill presentation");
            Check(battle.LastAction.contacts>=2 && battle.View.Feedback.ContactCount>=2,"the full shot contributes to settlement");
            Check(battle.View.Feedback.DisplayedDamage==battle.LastAction.normalDamage,"lethal shot keeps the complete damage total");
            Check(!battle.State.ActionPending && !battle.State.ShotInProgress && battle.State.Won==!enemy,"turn fully resolved with original outcome");
            long coinsAfter=battle.Progress.Coins;
            yield return new WaitForSeconds(.2f);
            Check(battle.Progress.Coins==coinsAfter,"result reward granted once");
            Capture(name+"-result");yield return null;
            battle.View.HideOverlay();battle.Restart();
            Check(!battle.State.Finished && !battle.State.ShotInProgress,"retry clears deferred defeat");
        }
        void PlaceContact(int index,Vector2 direction)
        {
            var charge=battle.Simulation.Charges[index];
            charge.Active=true;charge.Special=SpecialCharge.None;
            charge.Position=battle.Simulation.Position+direction*5;
        }
        IEnumerator CheckLethalSkill()
        {
            battle.Restart();battle.PlayerCard.actionSkill=ActionSkill.None;
            battle.PlayerCard.skill=Skill.Attack;battle.PlayerCard.skillPower=new[]{70f};
            foreach(var c in battle.Simulation.Charges)c.Active=false;
            battle.State.Enemy.Receive(battle.State.Enemy.MaxHp-1);
            Check(battle.Shoot(Vector2.up*200),"lethal skill fixture launch");
            float deadline=Time.realtimeSinceStartup+12;
            while(battle.State.Enemy.Hp>0 && Time.realtimeSinceStartup<deadline)yield return null;
            Check(battle.State.Enemy.Hp==0 && battle.Phase==BattlePhase.Impact && !battle.View.OverlayVisible,"lethal skill starts its presentation before the result");
            yield return new WaitForSeconds(tuning.impactSeconds*.4f);
            Check(battle.Phase==BattlePhase.Impact && !battle.View.OverlayVisible,"skill presentation is not cut short");
            while(battle.Phase!=BattlePhase.Finished && Time.realtimeSinceStartup<deadline)yield return null;
            Check(battle.Phase==BattlePhase.Finished && battle.State.Won,"lethal skill finishes at turn end");
            battle.View.HideOverlay();battle.Restart();
        }
        IEnumerator Run()
        {
            yield return new WaitForSeconds(.8f);
            Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Smoke","Feedback"));
            Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"Frames"));
            PrepareDurableCombatants();
            battle.View.HideOverlay();battle.Restart();
            // Isolate presentation so numeric feedback can be inspected at controlled values.
            battle.enabled=false;
            CheckHpBarShake();
            var feedback=battle.View.Feedback;
            feedback.BeginShot(false,Element.Fire);feedback.SetDamageCounterVisible(true);
            yield return new WaitForSeconds(.1f);
            Color yellow=feedback.DamageNumberColor,background=feedback.DamageCounterColor;
            var bankPosition=feedback.BankPosition;var bankRotation=feedback.BankRotation;
            var numberPosition=feedback.DamageNumberPosition;var restingScale=feedback.BankScale;
            Check(yellow.r>.7f && yellow.g>.7f && yellow.b<.2f,"damage digits are yellow");
            var bank=feedback.transform.Find("Damage counter");
            Check(bank!=null && bank.GetComponentsInChildren<TMP_Text>().Length==1,"counter contains damage number");
            Check(feedback.DisplayedDamage==0,"zero damage before first contact");
            int contact=0;
            foreach(int damage in new[]{32,33,34})
            {
                feedback.Contact(new Vector2(320,330),Element.Fire,true,++contact,0,damage);
                Check(feedback.DisplayedDamage==damage,"damage updates immediately, independently of hit count");
                Check(bank.GetComponentInChildren<TMP_Text>().text==damage.ToString(),"visible text is exact damage");
                Check(feedback.DamageNumberScale.x>1.2f && feedback.BankScale==restingScale,"only digits pop; burst is stationary");
                Check(feedback.DamageNumberPosition==numberPosition && feedback.BankPosition==bankPosition && feedback.BankRotation==bankRotation,"pop keeps all centers fixed");
                yield return new WaitForEndOfFrame();Capture("00-damage-"+damage+"-pop");yield return null;
                yield return new WaitForSeconds(.045f);
                Check(feedback.DamageNumberPosition!=numberPosition && feedback.BankPosition==bankPosition && feedback.BankScale==restingScale,"only digits shake");
                feedback.SetPaused(true);var frozen=feedback.DamageNumberScale;var frozenPosition=feedback.DamageNumberPosition;
                yield return new WaitForSeconds(.2f);Check(feedback.DamageNumberScale==frozen && feedback.DamageNumberPosition==frozenPosition,"pause freezes damage animation");
                feedback.SetPaused(false);yield return new WaitForSeconds(.3f);
                Check(feedback.DamageNumberScale==Vector3.one && feedback.BankScale==restingScale,"pop settles to same size");
                float centerError=Vector2.Distance(feedback.DamageNumberPosition,numberPosition);
                Debug.Log($"DAMAGE DIGIT REST error={centerError:R} UI units");
                Check(centerError<.001f,"digits return to center (within subpixel layout precision)");
                Check(feedback.DamageNumberColor.g<yellow.g && feedback.DamageCounterColor==background,"digits progress toward red; burst color stays fixed");
                Capture("00-damage-"+damage+"-settled");yield return null;
            }
            int[] damageSteps={0,499,500,999,1000,2499,2500,1000000,int.MaxValue};
            float[] scales={1,1,1.1f,1.1f,1.2f,1.4f,1.5f,1.5f,1.5f};
            for(int i=0;i<damageSteps.Length;i++)
            {
                feedback.BeginShot(false,Element.Fire);feedback.SetDamageCounterVisible(true);
                feedback.Contact(Vector2.zero,Element.Fire,false,1,0,damageSteps[i]);
                Check(Mathf.Abs(feedback.DamageSizeMultiplier-scales[i])<.0001f &&
                    Mathf.Abs(feedback.BankScale.x/restingScale.x-scales[i])<.0001f,"500 damage size boundary "+damageSteps[i]);
                Check(feedback.BankPosition==bankPosition && feedback.BankRotation==bankRotation,"growth keeps the burst center fixed");
                CheckDamageLayout((RectTransform)bank);
                yield return new WaitForSeconds(.25f);
                CheckDamageLayout((RectTransform)bank);
                Capture("00-size-"+damageSteps[i]);yield return null;
            }
            feedback.BeginShot(false,Element.Fire);feedback.SetDamageCounterVisible(true);
            for(int hits=4;hits<=128;hits++)feedback.Contact(Vector2.zero,Element.Fire,false,hits,0,hits*1000);
            Check(feedback.DisplayedDamage==128000,"rapid and refill contacts never lose accumulated damage");
            feedback.LockTotal(1234567);
            Check(feedback.DisplayedDamage==1234567 && bank.GetComponentInChildren<TMP_Text>().text==1234567.ToString("N0"),"locking shows the exact final damage");
            feedback.BeginShot(false,Element.Fire);
            feedback.SetDamageCounterVisible(true);
            feedback.Contact(Vector2.zero,Element.Fire,false,1,0,feedback.DamageColorFullAt/2);
            Color halfway=feedback.DamageNumberColor;
            yield return new WaitForSeconds(.25f);Capture("00-damage-half-orange");yield return null;
            feedback.Contact(Vector2.zero,Element.Fire,false,2,0,feedback.DamageColorFullAt);
            Color red=feedback.DamageNumberColor;
            Check(feedback.DamageCounterVisible,"color endpoint fixture is visible");
            Check(halfway.g<yellow.g && halfway.g>red.g && red.g<.15f,"yellow to orange to red follows damage");
            yield return new WaitForSeconds(.25f);Capture("00-damage-full-red");yield return null;
            feedback.BeginShot(true,Element.Water);
            Check(feedback.DisplayedDamage==0 && feedback.DamageNumberScale==Vector3.one && feedback.DamageNumberColor==yellow && feedback.BankScale==restingScale,"enemy shot resets damage, size, color and pulse");

            // Display a deterministic reference frame through the normal view and feedback APIs.
            var fixture=new ShotSimulation(tuning);
            foreach(var charge in fixture.Charges)charge.Active=false;
            fixture.Charges[0].Active=true;
            fixture.Charges[0].Element=battle.PlayerCard.element;
            fixture.Charges[0].Special=SpecialCharge.None;
            fixture.Charges[0].Position=fixture.Position+Vector2.up*5;
            Check(fixture.Launch(Vector2.up*200,battle.PlayerCard),"reference fixture launch");
            fixture.Tick(.001f);fixture.CancelShot();
            Check(fixture.Matched==1 && BattleRules.EffectLevel(fixture.Matched,tuning)==2,"reference fixture level two");
            for(int i=0;i<fixture.Charges.Length;i++)
            {
                var source=battle.Simulation.Charges[i];
                var target=fixture.Charges[i];
                target.Active=source.Active;target.Element=source.Element;
                target.Position=source.Position;target.Special=source.Special;
            }
            feedback.BeginShot(false,battle.PlayerCard.element);
            battle.View.Refresh(fixture,battle.Deck,battle.State,tuning,battle.SelectedSlot,true,
                battle.PlayerCard,battle.EnemyCard,BattlePhase.PlayerShot,false,battle.Progress);
            feedback.Contact(fixture.Charges[0].Position,battle.PlayerCard.element,true,32,1,1000);
            yield return new WaitForSeconds(1.2f);
            Check(feedback.DisplayedDamage==1000,"reference frame shows 1000 damage from 32 hits");
            var levelPanel=battle.View.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect=>rect.name=="EffectLevel");
            Check(levelPanel!=null,"left skill level panel visible");
            var levelTexts=levelPanel.GetComponentsInChildren<TMP_Text>();
            Check(levelTexts.Length==2 && levelTexts.Any(text=>text.text=="ノーマルスキル") &&
                levelTexts.Any(text=>text.text=="LV2"),"left panel shows normal skill and LV2");
            CheckLevelTextLayout(levelTexts);
            feedback.Contact(fixture.Charges[0].Position,battle.PlayerCard.element,true,33,1,2500);
            yield return new WaitForSeconds(.25f);CheckDamageLayout((RectTransform)bank);
            Capture("00d-reference-2500-damage-lv2");yield return null;
            Screen.SetResolution(480,800,FullScreenMode.Windowed);
            yield return new WaitForSeconds(.3f);
            CheckLevelTextLayout(levelTexts);
            CheckDamageLayout((RectTransform)bank);
            Capture("00e-reference-480x800");yield return null;
            Screen.SetResolution(720,900,FullScreenMode.Windowed);
            yield return new WaitForSeconds(.3f);
            CheckLevelTextLayout(levelTexts);
            CheckDamageLayout((RectTransform)bank);
            Capture("00f-reference-720x900");yield return null;
            Screen.SetResolution(540,960,FullScreenMode.Windowed);
            yield return new WaitForSeconds(.3f);
            battle.enabled=true;battle.Restart();
            Check(!battle.View.Feedback.DamageCounterVisible,"damage bank hidden before shot");
            Check(FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length==0,"no SE sources");
            // Session loadouts are cloned. This fixture does not edit the user's spirit assets.
            battle.PlayerCard.skill=Skill.Attack;battle.PlayerCard.skillPower=new[]{70f};
            int enemyHp=battle.State.Enemy.Hp,playerHp=battle.State.Player.Hp;
            var playerHpBar=(RectTransform)battle.View.transform.Find("Screen/PlayerBar");
            var enemyHpBar=(RectTransform)battle.View.transform.Find("Screen/Enemy/EnemyBar");
            // Use the actual resting layout; RectTransform can round authored coordinates.
            var playerHpBarHome=playerHpBar.anchoredPosition;var enemyHpBarHome=enemyHpBar.anchoredPosition;
            Debug.Log($"HP BAR BASELINES player=({playerHpBarHome.x:R},{playerHpBarHome.y:R}) enemy=({enemyHpBarHome.x:R},{enemyHpBarHome.y:R})");
            Debug.Log("FEEDBACK BUILD: flight-arrival HP settlement, target HP bar shake only, damage UI 500 damage/+10% max1.5, linear contact growth="+tuning.contactAttackFactor+", damageRedAt="+feedback.DamageColorFullAt);
            Capture("01-ready");yield return null;
            Check(battle.Shoot(new Vector2(100,450)),"launch");
            Check(battle.View.Feedback.DamageCounterVisible,"damage bank appears on shot");
            bool pausedOnce=false,pausedImpact=false,sawEffects=false;int shakenSides=0;
            int frame=0,contacts=0;float nextCapture=0,cutInAt=-1,flightAt=-1,deadline=Time.realtimeSinceStartup+40;
            BattlePhase prior=battle.Phase;
            while(Time.realtimeSinceStartup<deadline)
            {
                Check(battle.View.ScreenPosition==Vector2.zero,"screen remains stationary during shots and both damage stages");
                CheckHpBars();
                var phase=battle.Phase;bool captured=false;
                if(phase==BattlePhase.PlayerShot || phase==BattlePhase.EnemyShot || phase==BattlePhase.Charging || phase==BattlePhase.DamageFlight)
                {
                    bool enemy=battle.Simulation.EnemySide;
                    var target=enemy?battle.State.Player:battle.State.Enemy;
                    Check(target.Hp==(enemy?playerHp:enemyHp),"HP stays unchanged through shot, zone and damage flight");
                    Check(!battle.State.ActionPending && battle.State.ShotInProgress,"normal settlement waits for arrival");
                }
                if(phase==BattlePhase.DamageFlight || phase==BattlePhase.NormalImpact || phase==BattlePhase.CutIn)
                {
                    Check(!feedback.DamageCounterVisible,"flight and impact replace the counter without overlapping damage numbers");
                    if(phase!=BattlePhase.DamageFlight)
                    {
                        int before=battle.Simulation.EnemySide?playerHp:enemyHp;
                        int now=battle.Simulation.EnemySide?battle.State.Player.Hp:battle.State.Enemy.Hp;
                        Check(before-now==battle.LastAction.appliedNormalDamage && battle.State.ActionPending,
                            "normal damage applies once at arrival and is not repeated during impact or slide");
                    }
                }
                if(phase==BattlePhase.DamageFlight)
                    Check(battle.View.Feedback.DamageVisualsVisible,"damage flight is visible");
                if(phase==BattlePhase.NormalImpact)
                {
                    CheckImpactReadout(battle.LastAction.appliedNormalDamage);
                    bool enemy=battle.Simulation.EnemySide;
                    var bar=(RectTransform)battle.View.transform.Find(enemy?"Screen/PlayerBar":"Screen/Enemy/EnemyBar");
                    var home=enemy?playerHpBarHome:enemyHpBarHome;
                    if(bar.anchoredPosition!=home)shakenSides|=enemy?2:1;
                    if(!pausedImpact && battle.LastAction.appliedNormalDamage>0)
                    {
                        pausedImpact=true;battle.TogglePause();var frozenBar=bar.anchoredPosition;
                        int frozenHp=battle.State.Enemy.Hp;Capture("04a-hp-bar-shake-paused");
                        yield return new WaitForSeconds(.2f);
                        Check(bar.anchoredPosition==frozenBar && battle.State.Enemy.Hp==frozenHp,"pause freezes HP bar shake without repeated damage");
                        battle.TogglePause();
                    }
                }
                if(phase==BattlePhase.CutIn)
                {
                    Check(Vector2.Distance(playerHpBar.anchoredPosition,playerHpBarHome)<.001f && Vector2.Distance(enemyHpBar.anchoredPosition,enemyHpBarHome)<.001f,
                        $"both HP bars settle before character slide: player={playerHpBar.anchoredPosition} enemy={enemyHpBar.anchoredPosition}");
                }
                if(phase==BattlePhase.Impact)
                {
                    Check(!feedback.DamageCounterVisible,"skill readout stays clear of the accumulated damage counter");
                    var spirit=battle.Simulation.EnemySide?battle.EnemyCard:battle.PlayerCard;
                    if(spirit.skill==Skill.Attack || spirit.skill==Skill.PercentAttack)
                        CheckImpactReadout(battle.LastAction.appliedSkillDamage);
                }
                if(battle.Simulation.Collected>contacts && phase==BattlePhase.PlayerShot)
                {
                    contacts=battle.Simulation.Collected;
                    Check(battle.View.Feedback.ContactCount==contacts,"contact count");
                    Check(battle.View.Feedback.DisplayedDamage==battle.View.Feedback.DamageTarget,"real shot displays cumulative damage immediately");
                    Check(battle.View.Feedback.DamageTarget==BattleRules.ContactDamage(battle.PlayerCard.Attack*battle.State.Player.AttackMultiplier,battle.Simulation.DamageWeight,battle.PlayerCard.element,battle.State.Enemy.Element,tuning),"damage counter");
                    sawEffects|=battle.View.Feedback.ActiveBursts>0;
                    if(!pausedOnce)
                    {
                        pausedOnce=true;battle.TogglePause();float number=battle.View.Feedback.DisplayedDamage;
                        var position=battle.Simulation.Position;int bursts=battle.View.Feedback.ActiveBursts;
                        int frozenHp=battle.State.Enemy.Hp;
                        Capture("02-contact");captured=true;yield return new WaitForSeconds(.25f);
                        Check(battle.Simulation.Position==position && battle.View.Feedback.DisplayedDamage==number && battle.View.Feedback.ActiveBursts==bursts,"pause freezes effects");
                        Check(battle.State.Enemy.Hp==frozenHp,"pause does not settle the shot");
                        Check(battle.View.Feedback.DamageCounterVisible,"paused shot keeps damage bank");
                        battle.TogglePause();
                    }
                }
                if(phase!=prior)
                {
                    phases.Add(phase);prior=phase;Debug.Log($"FEEDBACK PHASE {phase} player={battle.State.Player.Hp} enemy={battle.State.Enemy.Hp} pending={battle.State.ActionPending}");
                    if(phase==BattlePhase.CutIn)Debug.Log($"HP BAR REST playerError={Vector2.Distance(playerHpBar.anchoredPosition,playerHpBarHome):R} enemyError={Vector2.Distance(enemyHpBar.anchoredPosition,enemyHpBarHome):R} UI units");
                    if(phase==BattlePhase.EnemyThinking)Check(!battle.View.Feedback.DamageCounterVisible,"enemy waiting hides damage bank");
                    if(phase==BattlePhase.EnemyShot)Check(battle.View.Feedback.DamageCounterVisible,"enemy shot shows damage bank");
                    if(phase==BattlePhase.NormalImpact || phase==BattlePhase.CutIn)
                    {
                        Check(battle.State.ActionPending,"pending normal action");
                        int before=battle.Simulation.EnemySide?playerHp:enemyHp;
                        int now=battle.Simulation.EnemySide?battle.State.Player.Hp:battle.State.Enemy.Hp;
                        Check(before-now==battle.LastAction.appliedNormalDamage,"normal-only HP loss before skill");
                    }
                    if(phase==BattlePhase.Impact)
                    {
                        Check(!battle.State.ActionPending,"skill completes action");
                        if(!battle.Simulation.EnemySide)Check(enemyHp-battle.State.Enemy.Hp==battle.LastAction.appliedDamage && battle.LastAction.appliedSkillDamage>0,"skill-only second HP loss");
                    }
                    if(!battle.Simulation.EnemySide)
                    {
                        if(phase==BattlePhase.DamageFlight)flightAt=Time.realtimeSinceStartup;
                        if(phase==BattlePhase.Charging){Capture("03-total");captured=true;}
                        if(phase==BattlePhase.NormalImpact){Capture("04-normal");captured=true;}
                        if(phase==BattlePhase.CutIn)cutInAt=Time.realtimeSinceStartup;
                        if(phase==BattlePhase.Impact){Capture("06-skill");captured=true;}
                    }
                }
                if(phase==BattlePhase.CutIn && cutInAt>=0 && Time.realtimeSinceStartup-cutInAt>tuning.cutInSeconds*.6f)
                {Capture("05-cutin");captured=true;cutInAt=-1;}
                if(phase==BattlePhase.DamageFlight && flightAt>=0 && Time.realtimeSinceStartup-flightAt>tuning.damageFlightSeconds*.5f)
                {Capture("03b-damage-flight");captured=true;flightAt=-1;}
                if(!captured && !battle.Simulation.EnemySide && Time.realtimeSinceStartup>=nextCapture && phase!=BattlePhase.Ready)
                {nextCapture=Time.realtimeSinceStartup+.05f;Capture("Frames/"+(frame++).ToString("D3"));}
                if(phase==BattlePhase.Ready && battle.State.Turn==2)break;
                yield return null;
            }
            var expected=new[]{BattlePhase.Charging,BattlePhase.DamageFlight,BattlePhase.NormalImpact,BattlePhase.CutIn,BattlePhase.Impact,BattlePhase.EnemyThinking,BattlePhase.EnemyShot,BattlePhase.Charging,BattlePhase.DamageFlight,BattlePhase.NormalImpact,BattlePhase.CutIn,BattlePhase.Impact,BattlePhase.Ready};
            Check(phases.Count==expected.Length,"phase count: "+string.Join(",",phases));
            for(int i=0;i<expected.Length;i++)Check(phases[i]==expected[i],"phase order "+i);
            Check(sawEffects && pausedOnce && battle.Simulation.ResetCount>=3,"effects, pause, action-end and optional all-clear refills");
            Check(pausedImpact && shakenSides==3,"target-only HP shake verified on both sides and while paused");
            Check(!battle.View.Feedback.DamageCounterVisible,"next turn hides damage bank");
            Capture("07-next-turn-hidden");
            yield return null;
            Check(battle.View.Feedback.ActiveBursts<=battle.View.Feedback.PoolCapacity,"bounded effect pool");
            battle.Restart();Check(battle.View.Feedback.DamageTarget==0 && battle.View.Feedback.ActiveBursts==0 && !battle.State.ActionPending,"restart cleanup");
            Check(!battle.View.Feedback.DamageCounterVisible,"retry hides damage bank");
            {
                float oldLimit=tuning.maximumShotSeconds,oldDeceleration=tuning.deceleration;
                tuning.maximumShotSeconds=1.5f;tuning.deceleration=1;
                yield return CheckDeferredDefeat(false);
                yield return CheckDeferredDefeat(true);
                yield return CheckLethalSkill();
                tuning.maximumShotSeconds=oldLimit;tuning.deceleration=oldDeceleration;
            }
            // Check effect objects/audio are released when the player exits during a shot.
            battle.Shoot(new Vector2(100,450));yield return new WaitForSeconds(.5f);
            var app=FindFirstObjectByType<PrototypeApp>();
            app.ReturnHome();
            // Destruction of this component cancels its coroutine; the app owns the final check.
            app.gameObject.AddComponent<FeedbackExitCheck>().Finish();
        }
    }
    public sealed class FeedbackExitCheck : MonoBehaviour
    {
        public void Finish()=>StartCoroutine(Check());
        IEnumerator Check()
        {
            yield return new WaitForSeconds(.4f);
            if(FindObjectsByType<BattleFeedback>(FindObjectsSortMode.None).Length!=0 || FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length!=0)
            {Debug.LogError("FEEDBACK SMOKE: home cleanup");Application.Quit(2);yield break;}
            Debug.Log("FEEDBACK SMOKE PASS: HP held through shot, zone and flight; applied once on damage arrival on both sides; short target-only HP bar shake, stationary screen/board/enemy panel, no shake for zero damage, exact bar reset and pause; cumulative damage UI, reference digit pop/shake, 500 damage/+10% max1.5, 3 resolutions, zero HP waits for complete impact/slide/skill, no early result/reward or following turn, retry and cleanup.");
            Application.Quit(0);
        }
    }
}
