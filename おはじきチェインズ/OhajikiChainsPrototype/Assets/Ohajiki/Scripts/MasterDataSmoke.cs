using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ohajiki
{
    public sealed class MasterDataSmoke : MonoBehaviour
    {
        PrototypeApp app;string output;
        public void Run(PrototypeApp owner){app=owner;StartCoroutine(Guard(Flow()));}
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        IEnumerator Guard(IEnumerator flow)
        {
            var stack=new Stack<IEnumerator>();stack.Push(flow);
            while(stack.Count>0)
            {
                object next;
                try{if(!stack.Peek().MoveNext()){stack.Pop();continue;}next=stack.Peek().Current;if(next is IEnumerator child){stack.Push(child);continue;}}
                catch(Exception e){Debug.LogError("MASTER SMOKE FAIL: "+e);Application.Quit(29);yield break;}
                yield return next;
            }
        }
        IEnumerator Capture(string name,GameObject root)
        {
            yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();UiSmokeChecks.Validate(root);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return null;yield return new WaitForEndOfFrame();
        }
        static void Click(Transform root, string name)
        {
            var button = root.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == name && b.IsInteractable());
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
        }
        IEnumerator VerifyBattleMenu(QuestDefinition quest)
        {
            var battle = app.Battle;
            var view = battle.View;
            var name = view.transform.Find("Screen/BattleHeader/StageName").GetComponent<TMP_Text>();
            Require(name.text == quest.DisplayTitle, "stage name follows selected quest");
            var menu = view.transform.Find("Screen/BattleMenu");
            Require(!menu.gameObject.activeSelf && battle.Shoot(Vector2.up * 300), "battle starts with menu closed");
            Click(view.transform, "MenuButton");
            Require(menu.gameObject.activeSelf && battle.Paused, "menu pauses an active shot");
            Vector2 stopped = battle.Simulation.Position;
            yield return Capture("05-battle-menu", view.gameObject);
            Require(battle.Simulation.Position == stopped, "shot stays still in menu");
            Click(menu, "ResumeButton");
            yield return new WaitForSeconds(.12f);
            Require(!menu.gameObject.activeSelf && !battle.Paused && battle.Simulation.Position != stopped, "menu resumes the same shot");
            Click(view.transform, "MenuButton");
            Click(menu, "RetryButton");
            Require(app.Battle == null && app.Hub.HasModal && app.Hub.SelectedQuestId == quest.id, "retry opens quest confirmation");
            Click(app.Hub.transform, "Button_決定");
            yield return new WaitForSeconds(.3f);
            Require(app.Battle != null && app.Battle.Phase == BattlePhase.Ready, "confirmed retry starts a fresh battle");
            Click(app.Battle.View.transform, "MenuButton");
            Click(app.Battle.View.transform, "ExitButton");
            yield return null;
            Require(app.Battle == null && app.Hub.Page == HubPage.QuestList, "retire returns to quest selection");
            Debug.Log("BATTLE MENU PASS: stage title, pause/resume shot, confirmed retry and retirement.");
        }
        IEnumerator Flow()
        {
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","SmokeMaster"));Directory.CreateDirectory(output);
            yield return new WaitForSeconds(.8f);Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            var data=app.Master;Require(data!=null&&data.sourceHash.Length==64,"generated master is loaded");
            Debug.Log("MASTER SNAPSHOT: SHA256 "+data.sourceHash+" / specialChargeProbabilities="+
                string.Join(",",data.battle.specialChargeProbabilities.Select(p=>p.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
            Debug.Log("MASTER ATTACKS: "+string.Join(",",data.home.catalog.Select(s=>s.baseAttack))+" / contactGrowth="+
                data.battle.contactAttackFactor.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            Require(ReferenceEquals(app.Session.Config,data.home),"session uses imported catalog");
            Require(data.home.catalog.Length==24&&data.home.quests.quests.Length==25&&data.npcs.Length==25,"master counts survive serialization");
            Require(data.home.catalog[0].Power(5)==180&&data.home.catalog[8].Power(5)==1.7f,"new skill values survive build");
            Require(data.home.catalog.All(s=>s.portrait==null),"unassigned textures stay null");
            Require(data.home.catalog[7].IsValid&&data.home.catalog[7].Power(5)==0,"null cleanse magnitude is usable");
            Require(data.battle.boardRows==8&&MasterConstants.GetInt("PlayerSession.initialOwnedCopies",99)==1,"constants loaded from bundle");
            yield return Capture("01-title-null-art",app.Title.gameObject);
            app.StartGame();yield return new WaitForSeconds(.3f);yield return Capture("02-home",app.Hub.gameObject);
            app.Hub.Navigate(HubPage.Gacha);yield return new WaitForSeconds(.2f);yield return Capture("03-gacha",app.Hub.gameObject);
            Require(Math.Abs(GachaRules.Probability(data.home,data.home.banners[0],0)-1.0/24)<1e-9,"master weight distribution");
            Require(app.Session.GachaCost(0,1)==1&&app.Session.GachaCost(0,10)==50,"per-banner imported prices");
            app.EnterQuest("QUEST0000_0003_LV0");yield return new WaitForSeconds(.4f);
            Require(app.Battle!=null&&app.Battle.EnemyCard.level==50,"NPC slot level50 enters battle");
            yield return Capture("04-battle-null-art",app.Battle.View.gameObject);
            yield return VerifyBattleMenu(data.home.quests.Find("QUEST0000_0003_LV0"));
            Require(app.Session.TryBeginQuest("QUEST0000_0000_LV0",out var run,out var reason),reason);
            long coins=app.Session.Progress.Coins;int gems=app.Session.Gems;
            var receipt=app.Session.CompleteQuest(run,true,data.battle);
            Require(receipt.Won&&app.Session.Progress.Coins==coins+500&&app.Session.Gems==gems+1,"master rewards and first clear");
            Require(app.Session.CompleteQuest(run,true,data.battle)==null,"no duplicate reward commit");
            Debug.Log("MASTER SMOKE PASS: imported snapshot, 24 spirits, 25 NPC decks and quests, null art/cleanse, skills, constants, weighted gachas/prices, NPC level50, rewards and idempotency.");
            Application.Quit(0);
        }
    }
}
