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
    // Explicit validation mode only. No fixture changes are made to source assets.
    public sealed class SpecificationSmoke : MonoBehaviour
    {
        PrototypeApp app;string output;
        public void Run(PrototypeApp owner){app=owner;StartCoroutine(Guard(Flow()));}
        IEnumerator Guard(IEnumerator flow)
        {
            var stack=new Stack<IEnumerator>();stack.Push(flow);
            while(stack.Count>0)
            {
                object next;
                try
                {
                    if(!stack.Peek().MoveNext()){stack.Pop();continue;}
                    next=stack.Peek().Current;
                    if(next is IEnumerator child){stack.Push(child);continue;}
                }
                catch(Exception error){Debug.LogError("SPEC SMOKE FAIL: "+error);Application.Quit(26);yield break;}
                yield return next;
            }
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Click(string name)
        {
            var root=app.Battle!=null?app.Battle.View.gameObject:app.Hub.gameObject;
            var button=root.GetComponentsInChildren<UnityEngine.UI.Button>().LastOrDefault(b=>b.IsInteractable()&&b.name=="Button_"+name);
            Require(button!=null,"missing button "+name);
            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();
            var root=app.Battle!=null?app.Battle.View.gameObject:app.Hub.gameObject;
            UiSmokeChecks.Validate(root);
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"one active EventSystem");
            Require(!root.GetComponentsInChildren<TMP_Text>().Any(t=>t.isTextOverflowing),"text overflow on "+name);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return null;yield return new WaitForEndOfFrame();
        }
        IEnumerator WinCurrent()
        {
            var battle=app.Battle;
            battle.State.Enemy.Receive(battle.State.Enemy.MaxHp-1);
            Require(battle.Shoot(new Vector2(90,450)),"launch playable quest");
            float deadline=Time.realtimeSinceStartup+35;
            while(battle.Phase!=BattlePhase.Finished&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(battle.Phase==BattlePhase.Finished&&battle.State.Won,"quest completes after a real shot");
        }
        IEnumerator Flow()
        {
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","SmokeSpecification"));Directory.CreateDirectory(output);
            yield return new WaitForSeconds(.8f);
            var session=app.Session;var quests=session.Config.quests.quests;
            Require(quests.Count(q=>q.category==QuestCategory.Event)==3,"3 event stages");
            Require(quests.Count(q=>q.category==QuestCategory.Showdown)==9,"3 showdown events x 3 difficulties");
            Require(quests.Count(q=>q.category==QuestCategory.Challenge)==5,"5 challenge floors");
            Click("スピリットバトル　→");yield return Capture("01-categories");
            foreach(QuestCategory category in Enum.GetValues(typeof(QuestCategory)))
            {app.Hub.OpenQuestCategory(category);yield return Capture("02-list-"+category);}
            var challenge=quests.First(q=>q.id=="challenge_01");
            app.Hub.OpenQuestCategory(QuestCategory.Challenge);yield return null;
            var scroll=app.Hub.GetComponentInChildren<UnityEngine.UI.ScrollRect>();scroll.verticalNormalizedPosition=0;
            yield return Capture("03-locked-deep-floors");
            Require(!session.QuestAvailable("challenge_02",out _),"next floor locked");
            app.Hub.OpenQuest("showdown_01_easy");Click("難");yield return Capture("04-hard-detail");
            Require(app.Hub.SelectedQuestId=="showdown_01_hard","difficulty selection");
            foreach(var size in new[]{new Vector2Int(480,800),new Vector2Int(720,900),new Vector2Int(540,960)})
            {Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);yield return Capture("05-detail-"+size.x+"x"+size.y);}
            // Energy confirmation, cancellation, insufficient gems and repeated confirmation.
            while(session.TryEnterBattle()){}
            int gems=session.Gems;app.Hub.RequestQuest("main_01");yield return Capture("06-refill");Click("戻る");
            Require(session.Energy==0&&session.Gems==gems,"refill cancellation preserves balances");
            if(session.Gems==0){app.Hub.RequestQuest("main_01");Click("決定");Require(session.Energy==0,"insufficient gems rejected");}
            Require(session.ApplyShop(ShopCurrency.Medals,0,new[]{new ShopReward{kind=ShopRewardKind.Gems,amount=100}},out _),"test funding");
            gems=session.Gems;app.Hub.RequestQuest("main_01");Click("決定");
            Require(session.Energy==session.MaximumEnergy&&session.Gems==gems-1,"one gem refill");
            Click("戻る");Require(app.Battle==null,"cancel sortie after refill");
            // Practice retirement before defeat is not a clear.
            app.EnterQuest("practice_01");yield return new WaitForSeconds(.2f);app.Battle.Retire();yield return null;
            Require(!session.IsCleared("practice_01"),"early practice retirement does not clear");
            int completed=0;
            foreach(var q in quests.Where(q=>!q.IsPractice).OrderBy(q=>q.category).ThenBy(q=>q.order).ThenBy(q=>q.difficulty))
            {
                if(session.Energy<q.energyCost)Require(session.TryRefillEnergy(out _),"energy refill for fixture");
                int beforeEnergy=session.Energy,beforeGems=session.Gems,beforeCopies=q.rewardSpiritIndex>=0?session.Copies(q.rewardSpiritIndex):0;
                long beforeXp=session.Progress.TotalExperience,beforeCoins=session.Progress.Coins;
                app.EnterQuest(q.id);app.EnterQuest(q.id);yield return new WaitForSeconds(.2f);
                Require(app.Battle!=null&&session.Energy==beforeEnergy-q.energyCost,"atomic entry "+q.id);
                yield return WinCurrent();
                Require(session.IsCleared(q.id)&&session.ActiveQuestRun==null,"clear committed "+q.id);
                Require(session.Progress.TotalExperience==beforeXp+q.experience&&session.Progress.Coins==beforeCoins+q.medals,"exact base rewards "+q.id);
                Require(session.Gems==beforeGems+q.gems+q.firstClearGems,"first-clear gems "+q.id);
                if(q.firstClearSpirit)Require(session.Copies(q.rewardSpiritIndex)==beforeCopies+1,"one spirit reward "+q.id);
                if(q.id=="main_01"||q.id=="event_03"||q.id=="showdown_01_hard"||q.id=="challenge_05")yield return Capture("07-result-"+q.id);
                app.ReturnHome();yield return null;completed++;Debug.Log("SPEC QUEST PASS: "+q.id);
            }
            Require(completed==24,"all 24 combat variants exercised");
            int eventPoints=session.EventPoints,allPower=session.MaterialCount(3);
            var exchange=session.Shop.Prepare("event_power",out string reason);
            Require(exchange!=null&&session.Shop.Confirm(exchange,out reason),"event reward exchange: "+reason);
            Require(session.EventPoints==eventPoints-30&&session.MaterialCount(3)==allPower+1,"event points reach exchange inventory");
            app.Hub.Navigate(HubPage.Shop);Click("イベント");yield return Capture("08-event-exchange");
            Require(session.MaximumEnergy==session.Config.maximumEnergy+session.Progress.Rank-1,"rank expands max energy");
            Require(session.QuestAvailable("challenge_05",out _),"final floor unlocked");
            app.Hub.OpenQuestCategory(QuestCategory.Challenge);yield return Capture("08-tower-cleared");
            // Replay has no repeat first-clear reward; returning after victory cannot award twice.
            if(session.Energy<1)session.TryRefillEnergy(out _);
            gems=session.Gems;app.EnterQuest("main_01");yield return new WaitForSeconds(.2f);yield return WinCurrent();
            Require(session.Gems==gems,"first-clear gems not repeated");app.ReturnHome();yield return null;
            // Defeat uses the actual battle terminal flow and gives no rewards.
            if(session.Energy<1)session.TryRefillEnergy(out _);
            long xp=session.Progress.TotalExperience,coins=session.Progress.Coins;
            app.EnterQuest("main_01");yield return new WaitForSeconds(.2f);
            app.Battle.State.Player.Receive(app.Battle.State.Player.MaxHp);app.Battle.Shoot(Vector2.up*400);
            yield return new WaitForSeconds(.2f);
            Require(app.Battle.Phase==BattlePhase.Finished&&session.Progress.TotalExperience==xp&&session.Progress.Coins==coins,"defeat gives no rewards");
            app.ReturnHome();yield return null;
            app.EnterQuest("practice_01");yield return new WaitForSeconds(.2f);
            var practice=app.Battle;practice.State.Enemy.Receive(practice.State.Enemy.MaxHp);
            Require(!practice.State.Finished&&practice.Shoot(Vector2.up*400),"practice continues at zero HP");
            float end=Time.realtimeSinceStartup+30;
            while(practice.Phase!=BattlePhase.Ready&&Time.realtimeSinceStartup<end)yield return null;
            Require(practice.Phase==BattlePhase.Ready&&!session.IsCleared("practice_01"),"no automatic practice clear");
            yield return Capture("09-practice-zero-hp");practice.Retire();yield return Capture("10-practice-clear");
            Require(session.IsCleared("practice_01")&&session.Progress.TotalExperience==xp&&session.Progress.Coins==coins,"retire clears practice without farm rewards");
            app.ReturnHome();yield return null;app.Hub.Navigate(HubPage.Settings);yield return Capture("11-settings");
            Debug.Log("SPEC SMOKE PASS: 24 combat variants, 3 event stages, 9 showdown difficulties, 5 sequential floors, reward/copy/energy transactions, replay, defeat, practice zero-HP/retirement, scroll, 3 resolutions, one EventSystem.");
            Application.Quit(0);
        }
    }
}
