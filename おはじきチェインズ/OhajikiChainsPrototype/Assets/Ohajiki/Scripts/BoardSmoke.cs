using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Ohajiki
{
    // Explicit --board-smoke-test only. The app owns this coroutine across battle teardown.
    public sealed class BoardSmoke : MonoBehaviour
    {
        PrototypeApp app;BattleTuning template;string output;
        public void Run(PrototypeApp owner,BattleTuning config){app=owner;template=config;StartCoroutine(Guard(Flow()));}
        IEnumerator Guard(IEnumerator flow)
        {
            var stack=new Stack<IEnumerator>();stack.Push(flow);
            while(stack.Count>0)
            {
                object next;
                try
                {
                    if(!stack.Peek().MoveNext()){stack.Pop();continue;}
                    next=stack.Peek().Current;if(next is IEnumerator child){stack.Push(child);continue;}
                }
                catch(Exception error){Debug.LogError("BOARD SMOKE FAIL: "+error);Application.Quit(27);yield break;}
                yield return next;
            }
        }
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        IEnumerator Until(Func<bool> predicate,float timeout,string message)
        {
            float deadline=Time.realtimeSinceStartup+timeout;
            while(!predicate()&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(predicate(),message);
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();
            ValidateLaunchClearance();
            UiSmokeChecks.Validate(app.Battle.View.gameObject);
            Require(!app.Battle.View.GetComponentsInChildren<TMP_Text>().Any(t=>t.isTextOverflowing),"text overflow "+name);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return null;yield return new WaitForEndOfFrame();
        }
        void ValidateLaunchClearance()
        {
            var board=app.Battle.View.Board;
            var playerZone=(RectTransform)board.Find("LaunchZone");
            var enemyZone=(RectTransform)board.Find("EnemyLaunchZone");
            float playerBoundary=Mathf.Max(playerZone.anchoredPosition.y+playerZone.rect.height,ShotSimulation.Height*template.launchInset+42);
            float enemyBoundary=enemyZone.anchoredPosition.y;
            var corners=new Vector3[4];
            foreach(var charge in board.GetComponentsInChildren<RectTransform>().Where(r=>r.name.StartsWith("Charge_",StringComparison.Ordinal)))
            {
                var rim=(RectTransform)charge.Find("Orb rim");rim.GetWorldCorners(corners);
                var local=corners.Select(p=>board.InverseTransformPoint(p)).ToArray();
                Require(local.Min(p=>p.y)>playerBoundary+8,"charge rim stays outside the full player shot area");
                Require(local.Max(p=>p.y)<enemyBoundary-8,"charge rim stays outside the enemy shot area");
            }
        }
        static void PlaceCascade(PrototypeBattle battle)
        {
            foreach(var c in battle.Simulation.Charges){c.Active=true;c.Element=battle.PlayerCard.element;c.Special=SpecialCharge.VerticalMissile;}
            battle.Simulation.Charges[0].Special=SpecialCharge.HorizontalMissile;
            battle.Simulation.Charges[9].Special=SpecialCharge.HorizontalMissile;
            battle.PlayerCard.actionSkill=ActionSkill.None;
            battle.Simulation.SetLaunchX(52);
        }
        IEnumerator Flow()
        {
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","SmokeBoard"));Directory.CreateDirectory(output);
            yield return new WaitForSeconds(.8f);
            // Hidden Windows smoke launches can have a black initial backbuffer until the first resize.
            Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            var battle=app.Battle;var sim=battle.Simulation;var feedback=battle.View.Feedback;
            Require(template.boardColumns==8&&template.boardRows==8&&sim.Charges.Length==64,"8 x 8 authored board");
            Require(battle.View.Board.GetComponentsInChildren<RectTransform>().Count(r=>r.name.StartsWith("Charge_",StringComparison.Ordinal))==64,"64 visible charge cells");
            yield return Capture("01-board-540x960");
            foreach(var size in new[]{new Vector2Int(480,800),new Vector2Int(720,900)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSeconds(.25f);
                yield return Capture("02-board-"+size.x+"x"+size.y);
            }
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.25f);
            // A deterministic first shot clears all 64 via missile chaining before the delay starts.
            // This smoke owns its temporary tuning/cards and destroys them after battle teardown.
            var durable=Instantiate(template);durable.deceleration=1;durable.maximumShotSeconds=14;
            durable.playerDeck=template.playerDeck.Select(s=>Instantiate(s)).ToArray();
            durable.enemyDeck=template.enemyDeck.Select(s=>Instantiate(s)).ToArray();
            // A board/refill fixture must survive its forced 64-hit missile cascade,
            // including when this build delivers damage immediately on contact.
            foreach(var spirit in durable.playerDeck.Concat(durable.enemyDeck))spirit.baseHp=50000;
            durable.specialChargeProbabilities=new float[6];
            battle.ConfigureSession(durable,null,app.Session.Progress);
            battle.Restart();sim=battle.Simulation;PlaceCascade(battle);
            Require(battle.Shoot(Vector2.up*300),"cascade launch");
            yield return Until(()=>sim.AllClearCount==1,2,"all-clear fires once after missile chain");
            Require(sim.Collected==64&&sim.Charges.All(c=>!c.Active),"all 64 consumed in one chain");
            yield return Until(()=>sim.AllClearWaitSeconds>=.65f,2,"banner reaches center");
            Require(feedback.AllClearVisible,"gold banner appears");
            var label=battle.View.GetComponentsInChildren<TMP_Text>().Single(t=>t.name=="AllClearText");
            Require(label.text=="全消し"&&label.color.r>.95f&&label.color.g>.7f&&label.color.b<.3f,"gold Japanese all-clear label");
            float x=feedback.AllClearPosition.x;yield return new WaitForSeconds(.08f);
            Require(feedback.AllClearPosition.x<x,"banner scrolls right to left");
            battle.TogglePause();float pausedAge=sim.AllClearWaitSeconds;var pausedPosition=feedback.AllClearPosition;
            yield return Capture("03-all-clear-gold");yield return new WaitForSeconds(.3f);
            Require(sim.AllClearWaitSeconds==pausedAge&&feedback.AllClearPosition==pausedPosition,"pause freezes refill and banner");
            battle.TogglePause();
            yield return Until(()=>!feedback.AllClearVisible,2,"banner completes");
            Require(sim.AllClearWaitSeconds>=1&&sim.AllClearWaitSeconds<=2,"banner lasts between one and two game seconds");
            yield return Until(()=>sim.AllClearWaitSeconds>=4.8f,5,"near five-second threshold");
            Require(sim.ResetCount==1&&sim.Charges.All(c=>!c.Active),"no early refill");
            yield return Until(()=>sim.ResetCount==2,1,"moving shot refills after five seconds");
            Require(sim.Moving&&sim.Collected>=64&&sim.Matched>=64,"refill preserves moving shot and accumulated hits");
            Require(battle.View.Board.GetComponentsInChildren<RectTransform>().Any(r=>r.name.StartsWith("Charge_",StringComparison.Ordinal)),"refilled balls become visible immediately");
            yield return Capture("04-refilled-mid-shot");
            battle.Restart();Require(!feedback.AllClearVisible&&!battle.Simulation.AwaitingAllClearRefill,"retry clears banner and timer");
            durable.maximumShotSeconds=1;PlaceCascade(battle);
            Require(battle.Shoot(Vector2.up*300),"short-shot launch");
            yield return Until(()=>!battle.Simulation.Moving,3,"shot ends before delay");
            Require(battle.Simulation.ResetCount==1&&!battle.Simulation.AwaitingAllClearRefill,"stopped shot has no delayed refill");
            app.ReturnHome();foreach(var spirit in durable.playerDeck.Concat(durable.enemyDeck))Destroy(spirit);Destroy(durable);
            yield return new WaitForSeconds(.3f);
            Require(FindObjectsByType<BattleFeedback>(FindObjectsSortMode.None).Length==0,"return destroys all-clear UI");
            Debug.Log("BOARD SMOKE PASS: 64 cells / 3 resolutions, missile-chain all-clear once, gold right-to-left banner 1-2 seconds, pause, five-second moving refill with visible cells and preserved hits, stopped-shot cancellation, retry and return cleanup.");
            Application.Quit(0);
        }
    }
}
