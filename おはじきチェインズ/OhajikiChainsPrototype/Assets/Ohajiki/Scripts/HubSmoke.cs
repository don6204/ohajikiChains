using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ohajiki
{
    // Explicit command-line validation only. Normal play never attaches this component.
    public sealed class HubSmoke : MonoBehaviour
    {
        PrototypeApp app;
        string directory;
        public void Run(PrototypeApp target) { app=target;StartCoroutine(Guard(Flow())); }
        IEnumerator Guard(IEnumerator flow)
        {
            while(true)
            {
                object current;
                try { if(!flow.MoveNext())yield break;current=flow.Current; }
                catch(Exception e) { Debug.LogError("HUB SMOKE FAIL: "+e);Application.Quit(10);yield break; }
                yield return current;
            }
        }
        void Require(bool condition,string description)
        { if(!condition)throw new InvalidOperationException(description); }
        void Click(string title,bool descendant=false)
        {
            var button=app.Hub.GetComponentsInChildren<Button>().LastOrDefault(b=>b.IsInteractable() &&
                (descendant?b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==title):b.name=="Button_"+title));
            Require(button!=null,"Button missing: "+title);button.onClick.Invoke();
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            UiSmokeChecks.Validate(app.Battle!=null?app.Battle.View.gameObject:app.Hub.gameObject);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));
            yield return null;yield return new WaitForEndOfFrame();
        }
        IEnumerator Flow()
        {
            directory=Path.Combine(Application.dataPath,"..","SmokeHome");Directory.CreateDirectory(directory);
            yield return new WaitForSeconds(.7f);
            var session=app.Session;var hub=app.Hub;
            Require(hub.Page==HubPage.Home,"home startup");
            Require(hub.GetComponentsInChildren<Button>().Count(b=>!b.interactable)==1,"settings placeholder");
            yield return Capture("01-home");
            Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);yield return Capture("01-home-480x800");
            Screen.SetResolution(720,900,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);yield return Capture("01-home-720x900");
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            Click("ガチャ");yield return null;
            var scroll=hub.GetComponentInChildren<ScrollRect>();Canvas.ForceUpdateCanvases();
            Require(scroll.content.rect.height>scroll.viewport.rect.height,"banners overflow for scrolling");
            scroll.verticalNormalizedPosition=0;yield return Capture("02-gacha-scroll");
            scroll.verticalNormalizedPosition=1;
            Click(session.Config.banners[0].title,true);yield return null;
            Click("排出率・排出内容");Require(hub.HasModal,"rate modal");
            Require(!hub.GetComponentsInChildren<Button>().First(b=>b.name=="Button_ホーム").IsInteractable(),"modal blocks background navigation");
            yield return Capture("03-rates");Click("閉じる");
            long coins=session.Progress.Coins;
            for(int i=0;i<2;i++)Require(session.Shop.Confirm(session.Shop.Prepare("gems_180",out _),out _),"test gem purchase");
            int gems=session.Gems,spent=0;
            hub.RequestGacha(1);Click("決定");yield return null;
            var movie=hub.GachaMovie;Require(movie!=null,"movie starts");
            int firstId=movie.Receipt.SpiritIds[0];spent+=movie.Receipt.Cost;
            movie.Skip();yield return new WaitForSeconds(.6f);yield return Capture("04-gacha-result");movie.Next();yield return null;
            int newId=-1;
            if(firstId>=session.Config.startingOwnedCount)newId=firstId;
            for(int i=0;i<60 && newId<0;i++)
            {
                Require(session.TryDraw(0,1,out var receipt,out _),"draw for deck fixture");int id=receipt.SpiritIds[0];spent+=receipt.Cost;
                if(id>=session.Config.startingOwnedCount)newId=id;
                yield return null;
            }
            Require(newId>=0,"gacha unlock with fixed test seed");
            Require(session.Progress.Coins==coins && session.Gems==gems-spent,"gacha currency costs");
            Click("スピリター");Click("デッキ編成　→");yield return null;
            int oldId=session.DeckCopy()[0];
            hub.ToggleDraft(oldId);hub.ToggleDraft(newId);
            Click("ホーム");Require(hub.HasModal,"discard confirmation");Click("戻る");
            Require(hub.Page==HubPage.Deck && session.DeckCopy()[0]==oldId,"cancel preserves saved deck");
            yield return Capture("05-deck");
            Click("この編成で決定　12/12");Click("決定");
            Require(session.DeckCopy().Contains(newId) && !session.DeckCopy().Contains(oldId),"saved deck");
            Click("スピリター強化　→");yield return null;
            Click(session.Config.catalog[newId].displayName,true);Click("強化アイテムを選択　→");
            Click("＋");yield return Capture("06-upgrade");
            int level=session.Level(newId);long cost=session.UpgradeCost(newId,2);
            Click("強化する");Click("戻る");Require(session.Level(newId)==level,"upgrade cancellation");
            Click("強化する");Click("決定");
            Require(session.Level(newId)==level+2 && session.Progress.Coins==coins-cost,"upgrade confirmation");
            Click("ホーム");Click("バトルへ　エネルギー "+session.Config.battleEnergyCost);yield return null;yield return null;
            Require(app.Battle!=null && app.Battle.State.Player.MaxHp==session.DeckHp,"battle deck and level transfer");
            Require(ReferenceEquals(app.Battle.Progress,session.Progress),"shared reward authority");
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"one battle EventSystem");
            yield return Capture("07-battle");
            // Terminal-state fixture: reduce enemy HP, then resolve an ordinary shot to exercise rewards.
            app.Battle.State.Enemy.Receive(app.Battle.State.Enemy.MaxHp-1);
            Require(app.Battle.Shoot(new Vector2(100,450)),"battle launch");
            float deadline=Time.realtimeSinceStartup+40;
            while(app.Battle.Phase!=BattlePhase.Finished && Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Battle.Phase==BattlePhase.Finished,"battle completion");
            Require(!app.Battle.View.Feedback.DamageBankVisible,"result hides damage bank");
            Require(session.Progress.Coins>coins-cost && session.Progress.TotalExperience>0,"reward applied");
            yield return Capture("08-result");
            app.ReturnHome();yield return null;
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"one home EventSystem");
            Require(hub.Page==HubPage.Home && session.DeckCopy().Contains(newId),"return keeps inventory");
            yield return Capture("09-home-return");
            // Repeated creation/teardown and energy exhaustion must not leave a second canvas or input module.
            int remaining=session.Energy;
            for(int i=0;i<remaining && session.Config.battleEnergyCost>0;i++)
            { app.EnterBattle();yield return null;app.ReturnHome();yield return null; }
            if(session.Config.battleEnergyCost>0)
            { app.EnterBattle();Require(app.Battle==null,"energy exhaustion blocks battle"); }
            Debug.Log("HUB SMOKE PASS: home, banner scroll, rates, paid-cost summon and skip, deck confirmation/cancel, upgrade confirmation/cancel, battle loadout, terminal-state rewards, return, repeated teardown, energy exhaustion.");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--stay-open")<0)Application.Quit(0);
        }
    }
}
