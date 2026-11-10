using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        IEnumerator Run()
        {
            yield return new WaitForSeconds(.8f);
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Smoke","Feedback"));
            Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"Frames"));
            battle.View.HideOverlay();battle.Restart();
            // Isolate the presentation to inspect low/mid/high colors without changing damage rules.
            battle.enabled=false;
            var feedback=battle.View.Feedback;
            feedback.BeginShot(false,Element.Fire);feedback.SetDamageBankVisible(true);
            yield return new WaitForSeconds(.1f);Color yellow=feedback.DamageBankColor;
            Capture("00a-color-yellow");yield return null;
            feedback.Contact(new Vector2(320,330),Element.Fire,true,10,0,tuning.damageColorFullAt/2);
            yield return new WaitForSeconds(.8f);Color orange=feedback.DamageBankColor;
            Capture("00b-color-orange");yield return null;
            feedback.Contact(new Vector2(320,330),Element.Fire,true,20,0,tuning.damageColorFullAt);
            yield return new WaitForSeconds(.8f);Color coral=feedback.DamageBankColor;
            Capture("00c-color-coral");yield return null;
            Check(yellow.g>orange.g && orange.g>coral.g,"increasing damage visibly warms the bank");
            Check(coral.g>=.42f && coral.b>=.33f,"high damage stays soft coral, not pure red");
            feedback.Contact(new Vector2(320,330),Element.Fire,true,30,0,tuning.damageColorFullAt*3);
            yield return new WaitForSeconds(.8f);
            Check(feedback.DamageBankColor==coral,"very high damage caps its color");
            feedback.BeginShot(true,Element.Water);
            Check(feedback.DamageBankColor==yellow,"enemy shot also resets to yellow");
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
                    Check(battle.View.Feedback.DamageTarget==BattleRules.ContactDamage(battle.PlayerCard.Attack,contacts,battle.PlayerCard.element,battle.State.Enemy.Element,tuning),"damage counter");
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
            Debug.Log("FEEDBACK SMOKE PASS: yellow/orange/coral damage colors, high-value cap, color reset, contacts, damage counter visibility, pause, normal HP before cut-in, skill after cut-in, both sides, two resets, retry and exit cleanup.");
            Application.Quit(0);
        }
    }
}
