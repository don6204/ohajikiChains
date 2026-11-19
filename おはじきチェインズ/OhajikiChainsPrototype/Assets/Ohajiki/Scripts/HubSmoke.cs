using System;
using System.Collections;
using System.Collections.Generic;
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
            var stack = new Stack<IEnumerator>();
            stack.Push(flow);
            while(stack.Count > 0)
            {
                object current;
                try
                {
                    var active = stack.Peek();
                    if (!active.MoveNext()) { stack.Pop(); continue; }
                    current = active.Current;
                    if (current is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch(Exception e)
                {
                    Debug.LogError("HUB SMOKE FAIL: "+e);
                    while(stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                    Application.Quit(10); yield break;
                }
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
        IEnumerator PointerClick(string title)
        {
            // GraphicRaycaster only considers graphics that have been registered and rendered.
            yield return null;
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
            var button = app.Hub.GetComponentsInChildren<Button>().Single(b => b.IsInteractable() && b.name == "Button_" + title);
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                button = PointerEventData.InputButton.Left
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            string first = hits.Count == 0 ? "none" : hits[0].gameObject.name + " depth=" + hits[0].depth;
            Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                $"pointer hits {title} at {pointer.position}; expected depth={button.GetComponent<Graphic>().depth}, first hit={first}");
            pointer.pointerCurrentRaycast = hits[0];
            pointer.pointerPressRaycast = hits[0];
            var target = hits[0].gameObject;
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerEnterHandler);
            var pressed = ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
            Require(pressed == button.gameObject, "button receives pointer press: " + title);
            ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerClickHandler);
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            UiSmokeChecks.Validate(app.Battle!=null?app.Battle.View.gameObject:app.Hub.gameObject);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));
            yield return null;yield return new WaitForEndOfFrame();
        }
        IEnumerator VerifyBattleHandInput()
        {
            var battle = app.Battle;
            var cards = battle.View.GetComponentsInChildren<HandCardPress>()
                .OrderBy(card => card.transform.position.x).ToArray();
            Require(cards.Length == SpiritDeck.HandSize, "four hand buttons are visible");
            int initialSlot = battle.SelectedSlot;
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            for (int step = 1; step <= cards.Length; step++)
            {
                int slot = (initialSlot + step) % cards.Length;
                var card = cards[slot];
                var rect = (RectTransform)card.transform;
                var pointer = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                    position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
                };
                hits.Clear();
                EventSystem.current.RaycastAll(pointer, hits);
                Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<HandCardPress>() == card,
                    "hand button receives the pointer at slot " + slot);
                var target = hits[0].gameObject;
                ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
                Require(!battle.View.DetailsVisible, "touch down alone does not open skill details");
                ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);
                Require(battle.SelectedSlot == slot && card.GetComponentsInChildren<TMP_Text>()
                    .Any(label => label.text.Contains(battle.PlayerCard.displayName)), "tap selects the named spirit");
                Require(cards.Count(entry => entry.transform.Find("Selected spirit").gameObject.activeSelf) == 1
                    && card.transform.Find("Selected spirit").gameObject.activeSelf, "only the selected card is marked");

                if (slot != initialSlot) continue;
                ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
                float holdSeconds = app.Master != null ? app.Master.battle.longPressSeconds : 1f;
                float deadline = Time.realtimeSinceStartup + Mathf.Max(5, holdSeconds * 3);
                while (!battle.View.DetailsVisible && Time.realtimeSinceStartup < deadline) yield return null;
                Require(battle.View.DetailsVisible, "holding the hand button opens skill details");
                var details = battle.View.transform.Find("Screen/SkillDetails").GetComponentsInChildren<TMP_Text>();
                Require(details.Any(label => label.text.Contains(battle.PlayerCard.displayName)
                    && label.text.Contains(battle.PlayerCard.skillName)), "details describe the held spirit");
                ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
                Require(!battle.View.DetailsVisible && battle.SelectedSlot == slot, "release dismisses details");
            }
            Debug.Log("HAND INPUT PASS: four buttons select their spirits, one active selection, hold reveals skill details and release dismisses them.");
        }
        void ValidateHeader()
        {
            var hub=app.Hub;var session=app.Session;
            var labels=hub.GetComponentsInChildren<TMP_Text>();
            Require(labels.Single(t=>t.name=="PlayerName").text==session.PlayerName,"header uses saved player name");
            Require(labels.Single(t=>t.name=="RankValue").text==session.Progress.Rank.ToString(),"header uses current rank");
            Require(labels.Single(t=>t.name=="MedalValue").text==session.Progress.Coins.ToString("N0"),"header uses current medals");
            Require(labels.Single(t=>t.name=="GemValue").text==session.Gems.ToString("N0"),"header uses current gems");
            var images=hub.GetComponentsInChildren<Image>();
            Require(Mathf.Abs(images.Single(i=>i.name=="EnergyFill").fillAmount-
                Mathf.Clamp01((float)session.Energy/session.MaximumEnergy))<.001f,"energy bar reflects the actual fraction");
            string[] names={"スピリター","ショップ","ホーム","ガチャ","設定"};
            float previous=float.MinValue;
            foreach(string name in names)
            {
                var button=hub.GetComponentsInChildren<Button>().Single(b=>b.name=="Button_"+name);
                float x=button.transform.position.x;
                Require(x>previous,"footer follows planner's navigation order");previous=x;
                Require(button.GetComponentsInChildren<Image>().Any(i=>i.sprite!=null && i.name.EndsWith("Icon")),"navigation artwork is imported: "+name);
            }
        }
        void VerifyHomeActions()
        {
            var hub = app.Hub;
            foreach (string name in new[] { "お知らせバナー", "お知らせ", "プレゼントBOX", "ミッション", "マルチプレイ" })
            {
                Click(name);
                string title = name == "お知らせバナー" ? "お知らせ" : name;
                Require(hub.HasModal && hub.GetComponentsInChildren<TMP_Text>().Any(label => label.text == title),
                    "home button opens " + title);
                Click("戻る");
                Require(!hub.HasModal && hub.Page == HubPage.Home, "return from " + title);
            }
            var buttons = hub.GetComponentsInChildren<Button>();
            Button Action(string name) => buttons.Single(button => button.name == "Button_" + name);
            var mission = Action("ミッション");
            var battle = Action("スピリットバトル　→");
            var multi = Action("マルチプレイ");
            Require(mission.transform.position.x < battle.transform.position.x && battle.transform.position.x < multi.transform.position.x,
                "home action order from left to right");
            foreach (var button in new[] { mission, battle, multi })
                Require(button.GetComponentsInChildren<Image>().Any(image => image.sprite != null),
                    "home action artwork: " + button.name);
        }
        IEnumerator VerifyHomeFavorites()
        {
            var hub = app.Hub;
            var session = app.Session;
            var ids = session.FindSpirits(-1, 0, 0).Where(id => id != session.Favorite(0)).Take(2).ToArray();
            Require(ids.Length == 2, "two owned spirits for the favorite carousel");
            int second = session.Favorite(1), third = session.Favorite(2);
            Require(session.SetFavorite(1, -1) && session.SetFavorite(2, -1), "start with one favorite");
            hub.Refresh();
            void VerifyFavorite(int slot, int id)
            {
                var label = hub.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "FeaturedName");
                var spirit = session.Config.catalog[id];
                Require(label.text == spirit.displayName, "show the current favorite name");
                var showcase = hub.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == "HomeShowcase");
                var portrait = showcase.GetComponentsInChildren<Image>(true).Single(image => image.name == "FeaturedPortrait");
                if (spirit.portrait != null)
                    Require(portrait.isActiveAndEnabled && portrait.sprite == spirit.portrait &&
                        portrait.rectTransform.rect.width > 0 && portrait.rectTransform.rect.height > 0,
                        "registered favorite portrait has visible area");
                else Require(!portrait.isActiveAndEnabled, "unregistered favorite shows its name only");
                var dots = showcase.GetComponentsInChildren<Image>().Where(image => image.isActiveAndEnabled &&
                    image.name.StartsWith("FavoriteDot", StringComparison.Ordinal)).OrderBy(image => image.transform.position.x).ToArray();
                var occupied = Enumerable.Range(0, 3).Where(i => session.Favorite(i) >= 0).ToArray();
                Require(dots.Length == occupied.Length, "one visible dot per configured favorite");
                for (int i = 0; i < dots.Length; i++)
                {
                    Require(dots[i].name == "FavoriteDot" + occupied[i], "favorite dots follow occupied slot order");
                    bool gold = dots[i].color.r > .9f && dots[i].color.g > .5f && dots[i].color.b < .45f;
                    Require(gold == (occupied[i] == slot), "only the selected favorite dot is gold");
                }
                float DotX(Image dot) => showcase.InverseTransformPoint(dot.rectTransform.TransformPoint(dot.rectTransform.rect.center)).x;
                Require(Mathf.Abs((DotX(dots[0]) + DotX(dots[dots.Length - 1])) * .5f - showcase.rect.center.x) < .1f,
                    "favorite dot group stays centered");
            }
            VerifyFavorite(0, session.Favorite(0));
            yield return PointerClick("FavoritePrevious");
            Require(hub.HasModal && hub.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "お気に入り 3 を選ぶ"),
                "previous arrow opens the last favorite slot");
            yield return PointerClick("戻る");
            Require(!hub.HasModal && session.Favorite(2) == -1, "cancel keeps the last slot empty");
            VerifyFavorite(0, session.Favorite(0));
            yield return PointerClick("FavoriteNext");
            Require(hub.HasModal && hub.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "お気に入り 2 を選ぶ"),
                "next arrow opens the second favorite slot");
            yield return PointerClick("FavoriteSpirit_" + ids[0]);
            Require(!hub.HasModal && hub.Page == HubPage.Home && session.Favorite(1) == ids[0], "pick a second favorite and return to home");
            VerifyFavorite(1, ids[0]);
            yield return PointerClick("FavoritePrevious");
            Require(session.SetFavorite(2, ids[1]), "fill third slot for cycling");
            // Exercise both artwork states even when the entire imported catalog has blank texture cells.
            var original = session.Config.catalog[ids[0]];
            var fixture = Instantiate(original);
            try
            {
                fixture.portrait = Resources.Load<Sprite>("UI/HomeScreen/Home");
                Require(fixture.portrait != null, "sample artwork for favorite portrait binding");
                session.Config.catalog[ids[0]] = fixture;
                yield return PointerClick("FavoriteNext"); VerifyFavorite(1, ids[0]);
                fixture.portrait = null;
                hub.Refresh();
                VerifyFavorite(1, ids[0]);
            }
            finally
            {
                session.Config.catalog[ids[0]] = original;
                Destroy(fixture);
            }
            yield return PointerClick("FavoriteNext"); VerifyFavorite(2, ids[1]);
            yield return PointerClick("FavoritePrevious"); VerifyFavorite(1, ids[0]);
            yield return PointerClick("FavoritePrevious");
            Require(session.SetFavorite(1, -1), "clear middle favorite");
            hub.Refresh();
            yield return PointerClick("FavoriteNext"); VerifyFavorite(2, ids[1]);
            yield return PointerClick("FavoriteNext");
            session.SetFavorite(1, second);
            session.SetFavorite(2, third);
            hub.Refresh();
            Debug.Log("HOME SHOWCASE PASS: pointer input on both arrows, empty-slot selection, cancellation, registered and blank portraits, page indicators and skipping empty slots.");
        }
        IEnumerator VerifyQuestCards()
        {
            var hub = app.Hub;
            var quests = app.Session.Config.quests.quests;
            foreach (QuestCategory category in Enum.GetValues(typeof(QuestCategory)))
            {
                string key = "QuestCategory_" + category;
                var button = hub.GetComponentsInChildren<Button>().Single(b => b.name == "Button_" + key);
                bool open = quests.Any(quest => quest.category == category && app.Session.QuestAvailable(quest.id, out _));
                Require(button.IsInteractable() == open, "category availability: " + category);
                if (!open) continue;
                yield return PointerClick(key);
                Require(hub.Page == HubPage.QuestList && hub.SelectedCategory == category, "category card opens " + category);
                yield return PointerClick("種類選択へ");
                Require(hub.Page == HubPage.Quests, "list returns to category selection");
            }
            var available = quests.Where(q => app.Session.QuestAvailable(q.id, out _)).ToArray();
            bool Preferred(QuestDefinition q) => q.category == QuestCategory.Event || q.category == QuestCategory.Showdown;
            IEnumerator VerifyBanner()
            {
                var illustrated = available.Where(q => q.banner != null).ToArray();
                var expected = illustrated.FirstOrDefault(Preferred) ?? illustrated.FirstOrDefault();
                if (expected == null)
                {
                    Require(!hub.GetComponentsInChildren<Button>().Any(b => b.name == "Button_QuestFeatured" && b.IsInteractable()),
                        "blank featured space has no clickable destination");
                    yield break;
                }
                yield return PointerClick("QuestFeatured");
                Require(hub.Page == HubPage.QuestDetail && hub.SelectedQuestId == expected.id, "open featured quest, preferring event artwork");
                hub.Navigate(HubPage.Quests);
            }
            yield return VerifyBanner();
            if (available.All(q => q.banner == null))
            {
                var fallback = available.FirstOrDefault(q => !Preferred(q));
                var preferred = available.FirstOrDefault(Preferred);
                var art = Resources.Load<Sprite>("UI/HomeScreen/Home");
                Require(art != null, "sample quest banner artwork");
                try
                {
                    if (fallback != null) { fallback.banner = art; hub.Refresh(); yield return VerifyBanner(); }
                    if (preferred != null) { preferred.banner = art; hub.Refresh(); yield return VerifyBanner(); }
                }
                finally
                {
                    if (fallback != null) fallback.banner = null;
                    if (preferred != null) preferred.banner = null;
                    hub.Navigate(HubPage.Quests);
                }
            }
            Debug.Log("QUEST MENU PASS: all six category cards follow availability and open their matching lists via pointer input.");
        }
        IEnumerator Flow()
        {
            directory=Path.Combine(Application.dataPath,"..","SmokeHome");Directory.CreateDirectory(directory);
            yield return new WaitForSeconds(.7f);
            var session=app.Session;var hub=app.Hub;
            Require(hub.Page==HubPage.Home,"home startup");
            Click("設定");Require(hub.Page==HubPage.Settings,"settings opens");Click("ホーム");Require(hub.Page==HubPage.Home,"settings returns home");
            ValidateHeader();
            VerifyHomeActions();
            Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);yield return Capture("01-home-480x800");
            Screen.SetResolution(720,900,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);yield return Capture("01-home-720x900");
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            yield return Capture("01-home");
            yield return VerifyHomeFavorites();
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
            Click("フィルター");Require(hub.HasModal,"filter selection screen");
            Click("火");Click("★1");yield return Capture("05a-filter");
            Click("決定");Require(!hub.HasModal && hub.Page==HubPage.Deck,"filter applies to deck editor");
            var filterButton=hub.GetComponentsInChildren<Button>().First(b=>b.name=="Button_フィルター");
            Require(filterButton.GetComponentInChildren<TMP_Text>().text.Contains("火 / ★1"),"selected filter summary");
            Click("フィルター");Click("水");Click("戻る");
            Require(filterButton.GetComponentInChildren<TMP_Text>().text.Contains("火 / ★1"),"cancel preserves filters");
            Click("ソート");Click("レア順");
            Require(hub.GetComponentsInChildren<Button>().First(b=>b.name=="Button_ソート").GetComponentInChildren<TMP_Text>().text.Contains("レア順"),"sort selection");
            Click("フィルター");Click("条件をリセット");Click("決定");
            Click("ソート");Click("名前順");
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
            Click("ホーム");yield return PointerClick("スピリットバトル　→");Require(hub.Page==HubPage.Quests,"quest menu");
            yield return VerifyQuestCards();
            foreach (var size in new[] { new Vector2Int(480, 800), new Vector2Int(720, 900), new Vector2Int(540, 960) })
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                yield return new WaitForSeconds(.3f);
                yield return Capture("06-quest-menu-" + size.x + "x" + size.y);
            }
            app.EnterBattle();yield return null;yield return null;
            Require(app.Battle!=null && app.Battle.State.Player.MaxHp==session.DeckHp,"battle deck and level transfer");
            Require(ReferenceEquals(app.Battle.Progress,session.Progress),"shared reward authority");
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"one battle EventSystem");
            yield return VerifyBattleHandInput();
            yield return Capture("07-battle");
            // Terminal-state fixture: reduce enemy HP, then resolve an ordinary shot to exercise rewards.
            app.Battle.State.Enemy.Receive(app.Battle.State.Enemy.MaxHp-1);
            Require(app.Battle.Shoot(new Vector2(100,450)),"battle launch");
            float deadline=Time.realtimeSinceStartup+40;
            while(app.Battle.Phase!=BattlePhase.Finished && Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Battle.Phase==BattlePhase.Finished,"battle completion");
            Require(!app.Battle.View.Feedback.DamageCounterVisible,"result hides damage bank");
            Require(session.Progress.Coins>coins-cost && session.Progress.TotalExperience>0,"reward applied");
            yield return Capture("08-result");
            app.ReturnHome();yield return null;
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"one home EventSystem");
            Require(hub.Page==HubPage.Home && session.DeckCopy().Contains(newId),"return keeps inventory");
            ValidateHeader();
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
