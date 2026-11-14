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
            while(target.Hp>0 && Time.realtimeSinceStartup<hitDeadline)yield return null;
            var shotPhase=enemy?BattlePhase.EnemyShot:BattlePhase.PlayerShot;
            Check(target.Hp==0 && battle.Phase==shotPhase && battle.Simulation.Moving,"HP zero keeps the current shot moving");
            Check(!battle.View.OverlayVisible && battle.State.ShotInProgress,"no result at lethal contact");
            Check(battle.State.Won==!enemy,"first zero HP records the correct winner");
            string name=enemy?"10-player-zero":"09-enemy-zero";
            Capture(name+"-during-shot");
            PlaceContact(1,battle.Simulation.Velocity.normalized);
            battle.TogglePause();Vector2 frozen=battle.Simulation.Position;
            yield return new WaitForSeconds(.2f);
            Check(battle.Phase==shotPhase && battle.Simulation.Position==frozen && !battle.View.OverlayVisible,"zero HP can pause without transitioning");
            battle.TogglePause();
            var actual=new List<BattlePhase>();var previous=shotPhase;
            float deadline=Time.realtimeSinceStartup+12;
            while(battle.Phase!=BattlePhase.Finished && Time.realtimeSinceStartup<deadline)
            {
                Check(!battle.View.OverlayVisible && battle.Progress.Coins==coinsBefore,"no scene/result or reward before turn completion");
                Check(battle.Phase!=BattlePhase.Ready && battle.Phase!=BattlePhase.EnemyThinking,"no following side starts after defeat");
                if(battle.Phase!=previous)
                {
                    previous=battle.Phase;actual.Add(previous);
                    if(previous==BattlePhase.CutIn)Capture(name+"-cutin");
                    if(previous==BattlePhase.Impact)Capture(name+"-skill");
                }
                yield return null;
            }
            Check(actual.SequenceEqual(new[]{BattlePhase.Charging,BattlePhase.CutIn,BattlePhase.Impact}),"lethal shot completes zone, slide and skill in order");
            Check(battle.Phase==BattlePhase.Finished && battle.View.OverlayVisible,"result opens only after the skill presentation");
            Check(battle.LastAction.contacts>=2 && battle.View.Feedback.DisplayedHits>=2,"contacts after zero HP still count");
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
            var feedback=battle.View.Feedback;
            feedback.BeginShot(false,Element.Fire);feedback.SetHitCounterVisible(true);
            yield return new WaitForSeconds(.1f);
            Color yellow=feedback.HitNumberColor,background=feedback.HitCounterColor;
            var bankPosition=feedback.BankPosition;var bankRotation=feedback.BankRotation;
            var numberPosition=feedback.HitNumberPosition;var restingScale=feedback.BankScale;
            Check(yellow.r>.7f && yellow.g>.7f && yellow.b<.2f,"hit digits are yellow");
            var bank=feedback.transform.Find("Hit counter");
            Check(bank!=null && bank.GetComponentsInChildren<TMP_Text>().Length==1,"counter contains hit number");
            Check(feedback.DisplayedHits==0,"zero hits before first contact");
            foreach(int hits in new[]{32,33,34})
            {
                feedback.Contact(new Vector2(320,330),Element.Fire,true,hits,0,99999);
                Check(feedback.DisplayedHits==hits,"hits update immediately, independently of damage");
                Check(bank.GetComponentInChildren<TMP_Text>().text==hits.ToString(),"visible text is exact hit count");
                Check(feedback.HitNumberScale.x>1.2f && feedback.BankScale==restingScale,"only digits pop; burst is stationary");
                Check(feedback.HitNumberPosition==numberPosition && feedback.BankPosition==bankPosition && feedback.BankRotation==bankRotation,"pop keeps all centers fixed");
                yield return new WaitForEndOfFrame();Capture("00-hit-"+hits+"-pop");yield return null;
                yield return new WaitForSeconds(.045f);
                Check(feedback.HitNumberPosition!=numberPosition && feedback.BankPosition==bankPosition && feedback.BankScale==restingScale,"only digits shake");
                feedback.SetPaused(true);var frozen=feedback.HitNumberScale;var frozenPosition=feedback.HitNumberPosition;
                yield return new WaitForSeconds(.2f);Check(feedback.HitNumberScale==frozen && feedback.HitNumberPosition==frozenPosition,"pause freezes hit animation");
                feedback.SetPaused(false);yield return new WaitForSeconds(.3f);
                Check(feedback.HitNumberScale==Vector3.one && feedback.BankScale==restingScale,"pop settles to same size");
                Check(feedback.HitNumberPosition==numberPosition,"digits return to center");
                Check(feedback.HitNumberColor.g<yellow.g && feedback.HitCounterColor==background,"digits progress toward red; burst color stays fixed");
                Capture("00-hit-"+hits+"-settled");yield return null;
            }
            for(int hits=35;hits<=128;hits++)feedback.Contact(Vector2.zero,Element.Fire,false,hits,0,1000000);
            Check(feedback.DisplayedHits==128,"rapid and refill contacts never lose hits");
            feedback.LockTotal(1234567);
            Check(feedback.DisplayedHits==128 && feedback.DamageTarget==1234567,"locking damage does not replace hits");
            feedback.BeginShot(false,Element.Fire);
            feedback.SetHitCounterVisible(true);
            feedback.Contact(Vector2.zero,Element.Fire,false,feedback.HitColorFullAt/2,0,1);
            Color halfway=feedback.HitNumberColor;
            yield return new WaitForSeconds(.25f);Capture("00-hit-half-orange");yield return null;
            feedback.Contact(Vector2.zero,Element.Fire,false,feedback.HitColorFullAt,0,1);
            Color red=feedback.HitNumberColor;
            Check(feedback.HitCounterVisible,"color endpoint fixture is visible");
            Check(halfway.g<yellow.g && halfway.g>red.g && red.g<.15f,"yellow to orange to red uses hits, not damage");
            yield return new WaitForSeconds(.25f);Capture("00-hit-full-red");yield return null;
            feedback.BeginShot(true,Element.Water);
            Check(feedback.DisplayedHits==0 && feedback.HitNumberScale==Vector3.one && feedback.HitNumberColor==yellow,"enemy shot resets hits, color and pulse");

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
            Check(feedback.DisplayedHits==32 && feedback.BankScale==restingScale,"reference frame shows 32 hits, not 1000 damage");
            var levelPanel=battle.View.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect=>rect.name=="EffectLevel");
            Check(levelPanel!=null,"left skill level panel visible");
            var levelTexts=levelPanel.GetComponentsInChildren<TMP_Text>();
            Check(levelTexts.Length==2 && levelTexts.Any(text=>text.text=="ノーマルスキル") &&
                levelTexts.Any(text=>text.text=="LV2"),"left panel shows normal skill and LV2");
            CheckLevelTextLayout(levelTexts);
            Capture("00d-reference-32-hits-lv2");yield return null;
            Screen.SetResolution(480,800,FullScreenMode.Windowed);
            yield return new WaitForSeconds(.3f);
            CheckLevelTextLayout(levelTexts);
            Capture("00e-reference-480x800");yield return null;
            Screen.SetResolution(720,900,FullScreenMode.Windowed);
            yield return new WaitForSeconds(.3f);
            CheckLevelTextLayout(levelTexts);
            Capture("00f-reference-720x900");yield return null;
            Screen.SetResolution(540,960,FullScreenMode.Windowed);
            yield return new WaitForSeconds(.3f);
            battle.enabled=true;battle.Restart();
            Check(!battle.View.Feedback.HitCounterVisible,"damage bank hidden before shot");
            Check(FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length==0,"no SE sources");
            // Session loadouts are cloned. This fixture does not edit the user's spirit assets.
            battle.PlayerCard.skill=Skill.Attack;battle.PlayerCard.skillPower=new[]{70f};
            int enemyHp=battle.State.Enemy.Hp,playerHp=battle.State.Player.Hp;
            Debug.Log("FEEDBACK BUILD: permanent contact damage, growth=1.02, hitRedAt="+feedback.HitColorFullAt);
            Capture("01-ready");yield return null;
            Check(battle.Shoot(new Vector2(100,450)),"launch");
            Check(battle.View.Feedback.HitCounterVisible,"damage bank appears on shot");
            bool pausedOnce=false,sawEffects=false;
            int frame=0,contacts=0;float nextCapture=0,cutInAt=-1,flightAt=-1,deadline=Time.realtimeSinceStartup+40;
            BattlePhase prior=battle.Phase;
            while(Time.realtimeSinceStartup<deadline)
            {
                Check(battle.View.ScreenPosition==Vector2.zero,"screen remains stationary during shots and both damage stages");
                var phase=battle.Phase;bool captured=false;
                if(phase==BattlePhase.PlayerShot || phase==BattlePhase.EnemyShot || phase==BattlePhase.Charging)
                {
                    bool enemy=battle.Simulation.EnemySide;
                    var source=enemy?battle.State.Enemy:battle.State.Player;
                    var target=enemy?battle.State.Player:battle.State.Enemy;
                    var spirit=enemy?battle.EnemyCard:battle.PlayerCard;
                    int raw=BattleRules.ContactDamage(spirit.Attack*source.AttackMultiplier,battle.Simulation.DamageWeight,spirit.element,target.Element,tuning);
                    Check((enemy?playerHp:enemyHp)-target.Hp==target.MitigatedDamage(raw),"contact-time HP equals cumulative contact damage for both sides");
                    Check(phase!=BattlePhase.Charging || !battle.State.ActionPending,"zone does not reapply contacts");
                }
                {
                    Check(phase!=BattlePhase.DamageFlight && phase!=BattlePhase.NormalImpact,"no damage presentation phases");
                    Check(!battle.View.DamageVisualsVisible,"no flight, damage readout or HP hit flash");
                }
                if(battle.Simulation.Collected>contacts && phase==BattlePhase.PlayerShot)
                {
                    contacts=battle.Simulation.Collected;
                    Check(battle.View.Feedback.ContactCount==contacts,"contact count");
                    Check(battle.View.Feedback.DisplayedHits==contacts,"real shot counter equals consumed charges");
                    Check(battle.View.Feedback.DamageTarget==BattleRules.ContactDamage(battle.PlayerCard.Attack*battle.State.Player.AttackMultiplier,battle.Simulation.DamageWeight,battle.PlayerCard.element,battle.State.Enemy.Element,tuning),"damage counter");
                    sawEffects|=battle.View.Feedback.ActiveBursts>0;
                    if(!pausedOnce)
                    {
                        pausedOnce=true;battle.TogglePause();float number=battle.View.Feedback.DisplayedHits;
                        var position=battle.Simulation.Position;int bursts=battle.View.Feedback.ActiveBursts;
                        int frozenHp=battle.State.Enemy.Hp;
                        Capture("02-contact");captured=true;yield return new WaitForSeconds(.25f);
                        Check(battle.Simulation.Position==position && battle.View.Feedback.DisplayedHits==number && battle.View.Feedback.ActiveBursts==bursts,"pause freezes effects");
                        Check(battle.State.Enemy.Hp==frozenHp,"pause freezes contact damage");
                        Check(battle.View.Feedback.HitCounterVisible,"paused shot keeps damage bank");
                        battle.TogglePause();
                    }
                }
                if(phase!=prior)
                {
                    phases.Add(phase);prior=phase;Debug.Log($"FEEDBACK PHASE {phase} player={battle.State.Player.Hp} enemy={battle.State.Enemy.Hp} pending={battle.State.ActionPending}");
                    if(phase==BattlePhase.EnemyThinking)Check(!battle.View.Feedback.HitCounterVisible,"enemy waiting hides damage bank");
                    if(phase==BattlePhase.EnemyShot)Check(battle.View.Feedback.HitCounterVisible,"enemy shot shows damage bank");
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
            var expected=new[]{BattlePhase.Charging,BattlePhase.CutIn,BattlePhase.Impact,BattlePhase.EnemyThinking,BattlePhase.EnemyShot,BattlePhase.Charging,BattlePhase.CutIn,BattlePhase.Impact,BattlePhase.Ready};
            Check(phases.Count==expected.Length,"phase count: "+string.Join(",",phases));
            for(int i=0;i<expected.Length;i++)Check(phases[i]==expected[i],"phase order "+i);
            Check(sawEffects && pausedOnce && battle.Simulation.ResetCount>=3,"effects, pause, action-end and optional all-clear refills");
            Check(!battle.View.Feedback.HitCounterVisible,"next turn hides damage bank");
            Capture("07-next-turn-hidden");
            yield return null;
            Check(battle.View.Feedback.ActiveBursts<=battle.View.Feedback.PoolCapacity,"bounded effect pool");
            battle.Restart();Check(battle.View.Feedback.DamageTarget==0 && battle.View.Feedback.ActiveBursts==0 && !battle.State.ActionPending,"restart cleanup");
            Check(!battle.View.Feedback.HitCounterVisible,"retry hides damage bank");
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
            Debug.Log("FEEDBACK SMOKE PASS: permanent contact damage with 1.02 growth, hit UI, zero HP continues shot/zone/slide/skill, no early result/reward or following turn, lethal skill completes, pause, retry and cleanup.");
            Application.Quit(0);
        }
    }
}
