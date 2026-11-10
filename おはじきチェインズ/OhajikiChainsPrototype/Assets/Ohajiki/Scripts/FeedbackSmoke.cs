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
        readonly List<BattlePhase> phases=new List<BattlePhase>();
        public void Begin(PrototypeBattle owner,BattleTuning config){battle=owner;tuning=config;StartCoroutine(Run());}
        void Check(bool condition,string message){if(!condition){Application.Quit(2);throw new InvalidOperationException("FEEDBACK SMOKE: "+message);}}
        void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
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
        IEnumerator Run()
        {
            yield return new WaitForSeconds(.8f);
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Smoke","Feedback"));
            Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"Frames"));
            battle.View.HideOverlay();battle.Restart();
            // Isolate presentation so numeric feedback can be inspected at controlled values.
            battle.enabled=false;
            var feedback=battle.View.Feedback;
            feedback.BeginShot(false,Element.Fire);feedback.SetDamageBankVisible(true);
            yield return new WaitForSeconds(.1f);
            Color red=feedback.DamageNumberColor,background=feedback.DamageBankColor;
            var bankPosition=feedback.BankPosition;var bankRotation=feedback.BankRotation;
            var numberPosition=feedback.DamageNumberPosition;
            Check(red.r>.7f && red.g<.35f && red.b<.35f,"damage digits start red");
            var bank=feedback.transform.Find("Damage bank");
            Check(bank!=null && bank.GetComponentsInChildren<TMP_Text>().Length==1,"burst contains only the damage number");
            Check(feedback.DisplayedDamage==0 && feedback.BankScale==Vector3.one*.55f,"zero damage uses small burst");
            int[] sampleValues={499,500,999,1000};
            float[] sampleScales={.55f,.78f,.78f,1};
            string[] sampleCaptures={"00a-size-small-499","00b-size-medium-500",null,"00c-size-max-1000"};
            for(int sample=0;sample<sampleValues.Length;sample++)
            {
                feedback.Contact(new Vector2(320,330),Element.Fire,true,sample+1,0,sampleValues[sample]);
                yield return new WaitForSeconds(.04f);
                if(sample==0)Check(feedback.DamageNumberPosition!=numberPosition,"contact shakes the digits");
                Check(feedback.BankPosition==bankPosition && feedback.BankRotation==bankRotation,"contact keeps bank position and angle fixed");
                yield return new WaitForSeconds(1.16f);
                Check(feedback.DisplayedDamage==sampleValues[sample],"damage counter reaches "+sampleValues[sample]);
                Check(feedback.BankScale==Vector3.one*sampleScales[sample],"damage burst scale at "+sampleValues[sample]);
                Check(feedback.DamageNumberColor==red && feedback.DamageBankColor==background,"staged burst keeps red digits and pink background");
                Check(feedback.BankPosition==bankPosition && feedback.BankRotation==bankRotation,"scaled bank keeps position and angle fixed");
                if(sampleCaptures[sample]!=null){Capture(sampleCaptures[sample]);yield return null;}
            }
            feedback.BeginShot(true,Element.Water);
            Check(feedback.DamageNumberColor==red,"enemy shot keeps red digits");
            Check(feedback.DisplayedDamage==0 && feedback.BankScale==Vector3.one*.55f,"enemy shot resets small burst");

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
            feedback.Contact(fixture.Charges[0].Position,battle.PlayerCard.element,true,1,1,1000);
            yield return new WaitForSeconds(1.2f);
            Check(feedback.DisplayedDamage==1000 && feedback.BankScale==Vector3.one,"reference fixture damage 1000 uses maximum burst");
            var levelPanel=battle.View.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect=>rect.name=="EffectLevel");
            Check(levelPanel!=null,"left skill level panel visible");
            var levelTexts=levelPanel.GetComponentsInChildren<TMP_Text>();
            Check(levelTexts.Length==2 && levelTexts.Any(text=>text.text=="ノーマルスキル") &&
                levelTexts.Any(text=>text.text=="LV2"),"left panel shows normal skill and LV2");
            CheckLevelTextLayout(levelTexts);
            Capture("00d-reference-1000-lv2");yield return null;
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
            Check(!battle.View.Feedback.DamageBankVisible,"damage bank hidden before shot");
            Check(FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length==0,"no SE sources");
            // Session loadouts are cloned. This fixture does not edit the user's spirit assets.
            battle.PlayerCard.skill=Skill.Attack;battle.PlayerCard.skillPower=new[]{70f};
            int enemyHp=battle.State.Enemy.Hp,playerHp=battle.State.Player.Hp;
            Capture("01-ready");yield return null;
            Check(battle.Shoot(new Vector2(100,450)),"launch");
            Check(battle.View.Feedback.DamageBankVisible,"damage bank appears on shot");
            bool pausedOnce=false,sawEffects=false;
            int frame=0,contacts=0;float nextCapture=0,cutInAt=-1,flightAt=-1,deadline=Time.realtimeSinceStartup+40;
            BattlePhase prior=battle.Phase;
            while(Time.realtimeSinceStartup<deadline)
            {
                var phase=battle.Phase;bool captured=false;
                if(!battle.Simulation.EnemySide && (phase==BattlePhase.PlayerShot || phase==BattlePhase.Charging || phase==BattlePhase.DamageFlight))
                    Check(battle.State.Enemy.Hp==enemyHp,"damage applied before final count");
                if(battle.Simulation.Collected>contacts && phase==BattlePhase.PlayerShot)
                {
                    contacts=battle.Simulation.Collected;
                    Check(battle.View.Feedback.ContactCount==contacts,"contact count");
                    Check(battle.View.Feedback.DamageTarget==BattleRules.ContactDamage(battle.PlayerCard.Attack*battle.State.Player.AttackMultiplier,battle.Simulation.WeightedContacts,battle.PlayerCard.element,battle.State.Enemy.Element,tuning),"damage counter");
                    sawEffects|=battle.View.Feedback.ActiveBursts>0;
                    if(!pausedOnce)
                    {
                        pausedOnce=true;battle.TogglePause();float number=battle.View.Feedback.DisplayedDamage;
                        var position=battle.Simulation.Position;int bursts=battle.View.Feedback.ActiveBursts;
                        Capture("02-contact");captured=true;yield return new WaitForSeconds(.25f);
                        Check(battle.Simulation.Position==position && battle.View.Feedback.DisplayedDamage==number && battle.View.Feedback.ActiveBursts==bursts,"pause freezes effects");
                        Check(battle.View.Feedback.DamageBankVisible,"paused shot keeps damage bank");
                        battle.TogglePause();
                    }
                }
                if(phase!=prior)
                {
                    phases.Add(phase);prior=phase;Debug.Log($"FEEDBACK PHASE {phase} player={battle.State.Player.Hp} enemy={battle.State.Enemy.Hp} pending={battle.State.ActionPending}");
                    if(phase==BattlePhase.EnemyThinking)Check(!battle.View.Feedback.DamageBankVisible,"enemy waiting hides damage bank");
                    if(phase==BattlePhase.EnemyShot)Check(battle.View.Feedback.DamageBankVisible,"enemy shot shows damage bank");
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
            Check(sawEffects && pausedOnce && battle.Simulation.ResetCount==3,"effects, pause, board reset");
            Check(!battle.View.Feedback.DamageBankVisible,"next turn hides damage bank");
            Capture("07-next-turn-hidden");
            yield return null;
            Check(battle.View.Feedback.ActiveBursts<=battle.View.Feedback.PoolCapacity,"bounded effect pool");
            battle.Restart();Check(battle.View.Feedback.DamageTarget==0 && battle.View.Feedback.ActiveBursts==0 && !battle.State.ActionPending,"restart cleanup");
            Check(!battle.View.Feedback.DamageBankVisible,"retry hides damage bank");
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
            Debug.Log("FEEDBACK SMOKE PASS: fixed red digits, 499/500/999/1000 burst scale boundaries, fixed bank position and angle, digits-only counter, reference 1000 and LV2 frame, contacts, damage counter visibility, pause, normal HP before cut-in, skill after cut-in, both sides, two resets, retry and exit cleanup.");
            Application.Quit(0);
        }
    }
}
