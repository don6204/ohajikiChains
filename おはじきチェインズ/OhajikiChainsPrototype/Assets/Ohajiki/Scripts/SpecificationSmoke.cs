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
        static long Balance(PlayerSession session,ShopRewardKind kind)
        {
            switch(kind)
            {
                case ShopRewardKind.Medals:return session.Progress.Coins;
                case ShopRewardKind.Gems:return session.Gems;
                case ShopRewardKind.ShiningStones:return session.ShiningStones;
                case ShopRewardKind.EventPoints:return session.EventPoints;
                default:return session.MaterialCount((int)kind-(int)ShopRewardKind.FirePower);
            }
        }
        static long[] Balances(PlayerSession session)=>Enum.GetValues(typeof(ShopRewardKind)).Cast<ShopRewardKind>().Select(kind=>Balance(session,kind)).ToArray();
        static int[] Copies(PlayerSession session)=>Enumerable.Range(0,session.Config.catalog.Length).Select(session.Copies).ToArray();
        static void VerifyRewards(PlayerSession session,QuestDefinition quest,bool first,long[] before,int[] copies)
        {
            foreach(ShopRewardKind kind in Enum.GetValues(typeof(ShopRewardKind)))
            {
                long minimum,maximum;
                if(quest.useMasterRewards)
                {
                    var rows=quest.masterRewards.Where(r=>r.spiritIndex<0&&r.itemKind==kind).ToArray();
                    minimum=rows.Where(r=>r.probability>=1).Sum(r=>(long)r.min);
                    maximum=rows.Where(r=>r.probability>0).Sum(r=>(long)r.max);
                    if(first&&kind==ShopRewardKind.Gems){minimum+=quest.firstClearGems;maximum+=quest.firstClearGems;}
                }
                else minimum=maximum=kind==ShopRewardKind.Medals?quest.medals:quest.Rewards(first).Where(r=>r.kind==kind).Sum(r=>(long)r.amount);
                long gain=Balance(session,kind)-before[(int)kind];
                Require(gain>=minimum&&gain<=maximum,"item reward "+kind+" "+quest.id+": "+gain+" expected "+minimum+".."+maximum);
            }
            var guaranteed=quest.useMasterRewards&&first&&quest.firstClearSpirit?quest.masterRewards.FirstOrDefault(r=>r.spiritIndex>=0&&
                (quest.rewardSpiritIndex<0||r.spiritIndex==quest.rewardSpiritIndex)):null;
            for(int id=0;id<copies.Length;id++)
            {
                long minimum=0,maximum=0;
                if(quest.useMasterRewards)
                    foreach(var row in quest.masterRewards.Where(r=>r.spiritIndex==id))
                    {
                        if(ReferenceEquals(row,guaranteed)){minimum++;maximum++;}
                        else {if(row.probability>=1)minimum+=row.min;if(row.probability>0)maximum+=row.max;}
                    }
                else if(quest.rewardSpiritIndex==id)
                {minimum=first&&quest.firstClearSpirit||quest.spiritDropChance>=1?1:0;maximum=minimum>0||quest.spiritDropChance>0?1:0;}
                int gain=session.Copies(id)-copies[id];
                Require(gain>=minimum&&gain<=maximum,"spirit reward "+id+" "+quest.id+": "+gain+" expected "+minimum+".."+maximum);
            }
        }
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
            var challenges=quests.Where(q=>q.category==QuestCategory.Challenge).OrderBy(q=>q.order).ToArray();
            var main=quests.Where(q=>q.category==QuestCategory.Main).OrderBy(q=>q.order).First();
            var practiceQuest=quests.Single(q=>q.IsPractice);
            var easy=quests.Where(q=>q.category==QuestCategory.Showdown&&q.difficulty==QuestDifficulty.Easy).OrderBy(q=>q.order).First();
            var hard=quests.Single(q=>q.groupId==easy.groupId&&q.difficulty==QuestDifficulty.Hard);
            var lastEvent=quests.Where(q=>q.category==QuestCategory.Event).OrderBy(q=>q.order).Last();
            var resultCaptures=new HashSet<string>{main.id,lastEvent.id,hard.id,challenges.Last().id};
            app.Hub.OpenQuestCategory(QuestCategory.Challenge);yield return null;
            var scroll=app.Hub.GetComponentInChildren<UnityEngine.UI.ScrollRect>();scroll.verticalNormalizedPosition=0;
            yield return Capture("03-locked-deep-floors");
            Require(!session.QuestAvailable(challenges[1].id,out _),"next floor locked");
            app.Hub.OpenQuest(easy.id);Click("難");yield return Capture("04-hard-detail");
            Require(app.Hub.SelectedQuestId==hard.id,"difficulty selection");
            foreach(var size in new[]{new Vector2Int(480,800),new Vector2Int(720,900),new Vector2Int(540,960)})
            {Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);yield return Capture("05-detail-"+size.x+"x"+size.y);}
            // Energy confirmation, cancellation, insufficient gems and repeated confirmation.
            while(session.TryEnterBattle()){}
            int gems=session.Gems;app.Hub.RequestQuest(main.id);yield return Capture("06-refill");Click("戻る");
            Require(session.Energy==0&&session.Gems==gems,"refill cancellation preserves balances");
            if(session.Gems==0){app.Hub.RequestQuest(main.id);Click("決定");Require(session.Energy==0,"insufficient gems rejected");}
            Require(session.ApplyShop(ShopCurrency.Medals,0,new[]{new ShopReward{kind=ShopRewardKind.Gems,amount=100}},out _),"test funding");
            gems=session.Gems;app.Hub.RequestQuest(main.id);Click("決定");
            Require(session.Energy==session.MaximumEnergy&&session.Gems==gems-session.Config.energyRefillGems,"configured gem refill");
            Click("戻る");Require(app.Battle==null,"cancel sortie after refill");
            // Practice retirement before defeat is not a clear.
            app.EnterQuest(practiceQuest.id);yield return new WaitForSeconds(.2f);app.Battle.Retire();yield return null;
            Require(!session.IsCleared(practiceQuest.id),"early practice retirement does not clear");
            int completed=0;
            foreach(var q in quests.Where(q=>!q.IsPractice).OrderBy(q=>q.category).ThenBy(q=>q.order).ThenBy(q=>q.difficulty))
            {
                if(session.Energy<q.energyCost)Require(session.TryRefillEnergy(out _),"energy refill for fixture");
                int beforeEnergy=session.Energy;var beforeBalances=Balances(session);var beforeCopies=Copies(session);
                long beforeXp=session.Progress.TotalExperience;
                app.EnterQuest(q.id);app.EnterQuest(q.id);yield return new WaitForSeconds(.2f);
                Require(app.Battle!=null&&session.Energy==beforeEnergy-q.energyCost,"atomic entry "+q.id);
                yield return WinCurrent();
                Require(session.IsCleared(q.id)&&session.ActiveQuestRun==null,"clear committed "+q.id);
                Require(session.Progress.TotalExperience==beforeXp+q.experience,"exact experience reward "+q.id);
                VerifyRewards(session,q,true,beforeBalances,beforeCopies);
                if(resultCaptures.Contains(q.id))yield return Capture("07-result-"+q.id);
                app.ReturnHome();yield return null;completed++;Debug.Log("SPEC QUEST PASS: "+q.id);
            }
            Require(completed==24,"all 24 combat variants exercised");
            int eventPoints=session.EventPoints,allPower=session.MaterialCount(3);
            var exchange=session.Shop.Prepare("event_power",out string reason);
            Require(exchange!=null&&session.Shop.Confirm(exchange,out reason),"event reward exchange: "+reason);
            Require(session.EventPoints==eventPoints-30&&session.MaterialCount(3)==allPower+1,"event points reach exchange inventory");
            app.Hub.Navigate(HubPage.Shop);Click("イベント");yield return Capture("08-event-exchange");
            Require(session.MaximumEnergy==session.Config.maximumEnergy+(session.Progress.Rank-1)*session.Config.energyPerRank,"rank expands max energy");
            Require(session.QuestAvailable(challenges.Last().id,out _),"final floor unlocked");
            app.Hub.OpenQuestCategory(QuestCategory.Challenge);yield return Capture("08-tower-cleared");
            // Replay has no repeat first-clear reward; returning after victory cannot award twice.
            if(session.Energy<main.energyCost)session.TryRefillEnergy(out _);
            var replayBalances=Balances(session);var replayCopies=Copies(session);
            app.EnterQuest(main.id);yield return new WaitForSeconds(.2f);yield return WinCurrent();
            VerifyRewards(session,main,false,replayBalances,replayCopies);
            var clearedBalances=Balances(session);var clearedCopies=Copies(session);
            app.ReturnHome();yield return null;
            Require(clearedBalances.SequenceEqual(Balances(session))&&clearedCopies.SequenceEqual(Copies(session)),"return does not award twice");
            // Defeat uses the actual battle terminal flow and gives no rewards.
            if(session.Energy<main.energyCost)session.TryRefillEnergy(out _);
            long xp=session.Progress.TotalExperience,coins=session.Progress.Coins;
            app.EnterQuest(main.id);yield return new WaitForSeconds(.2f);
            app.Battle.State.Player.Receive(app.Battle.State.Player.MaxHp);app.Battle.Shoot(Vector2.up*400);
            yield return new WaitForSeconds(.2f);
            Require(app.Battle.Phase==BattlePhase.Finished&&session.Progress.TotalExperience==xp&&session.Progress.Coins==coins,"defeat gives no rewards");
            app.ReturnHome();yield return null;
            app.EnterQuest(practiceQuest.id);yield return new WaitForSeconds(.2f);
            var practice=app.Battle;practice.State.Enemy.Receive(practice.State.Enemy.MaxHp);
            Require(!practice.State.Finished&&practice.Shoot(Vector2.up*400),"practice continues at zero HP");
            float end=Time.realtimeSinceStartup+30;
            while(practice.Phase!=BattlePhase.Ready&&Time.realtimeSinceStartup<end)yield return null;
            Require(practice.Phase==BattlePhase.Ready&&!session.IsCleared(practiceQuest.id),"no automatic practice clear");
            yield return Capture("09-practice-zero-hp");practice.Retire();yield return Capture("10-practice-clear");
            Require(session.IsCleared(practiceQuest.id)&&session.Progress.TotalExperience==xp&&session.Progress.Coins==coins,"retire clears practice without farm rewards");
            app.ReturnHome();yield return null;app.Hub.Navigate(HubPage.Settings);yield return Capture("11-settings");
            Debug.Log("SPEC SMOKE PASS: 24 combat variants, 3 event stages, 9 showdown difficulties, 5 sequential floors, reward/copy/energy transactions, replay, defeat, practice zero-HP/retirement, scroll, 3 resolutions, one EventSystem.");
            Application.Quit(0);
        }
    }
}
