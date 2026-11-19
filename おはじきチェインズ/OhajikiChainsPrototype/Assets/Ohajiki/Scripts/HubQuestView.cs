using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Ohajiki
{
    public sealed partial class HubView
    {
        public QuestCategory SelectedCategory { get; private set; }
        public string SelectedQuestId { get; private set; }
        static readonly string[] QuestDescriptions={"冒険を進めて経験値を集めよう","期間限定の冒険とイベントポイント","易・普通・難に挑み、仲間を獲得","属性別パワーでスピリターを強化","5つの階層に挑戦。奥ほど強敵が待つ","敵HPが0になったらリタイアでクリア"};
        public void OpenQuestCategory(QuestCategory category)
        { SelectedCategory=category;Navigate(HubPage.QuestList); }
        public void OpenQuest(string id)
        { SelectedQuestId=id;Navigate(HubPage.QuestDetail); }
        void QuestMenu()
        {
            var board = skin.Image(content, "QuestMenuBoard", Resources.Load<Sprite>("UI/QuestMenuBoard"),
                -32, -30, 720, 1026, Color.white).rectTransform;
            QuestCategoryButton(board, QuestCategory.Main, new Rect(290, 362, 378, 501));
            QuestCategoryButton(board, QuestCategory.Showdown, new Rect(36, 385, 241, 339));
            QuestCategoryButton(board, QuestCategory.Challenge, new Rect(683, 385, 242, 339));
            QuestCategoryButton(board, QuestCategory.Materials, new Rect(36, 747, 241, 382));
            QuestCategoryButton(board, QuestCategory.Event, new Rect(290, 880, 378, 249));
            QuestCategoryButton(board, QuestCategory.Practice, new Rect(683, 747, 242, 382));

            var featured = session.Config.quests.quests.Where(q => q.banner != null && session.QuestAvailable(q.id, out _))
                .OrderByDescending(q => q.category == QuestCategory.Event || q.category == QuestCategory.Showdown)
                .FirstOrDefault();
            if (featured != null)
            {
                var banner = QuestMenuImage(board, "Button_QuestFeatured", featured.banner, new Rect(58, 65, 843, 245));
                banner.preserveAspect = true;
                ChromeButton(banner, () => OpenQuest(featured.id));
            }
        }

        // The card and banner bounds are measured from the top-left of the 960 x 1292 menu artwork.
        Image QuestMenuImage(RectTransform board, string name, Sprite sprite, Rect bounds)
        {
            var image = skin.Image(board, name, sprite, 0, 0, 0, 0, Color.white);
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(bounds.xMin / 960f, 1 - bounds.yMax / 1292f);
            rect.anchorMax = new Vector2(bounds.xMax / 960f, 1 - bounds.yMin / 1292f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return image;
        }

        void QuestCategoryButton(RectTransform board, QuestCategory category, Rect bounds)
        {
            var overlay = QuestMenuImage(board, "Button_QuestCategory_" + category, skin.FlatPlate, bounds);
            overlay.type = Image.Type.Sliced;
            var button = ChromeButton(overlay, () => OpenQuestCategory(category));
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = Color.clear;
            colors.highlightedColor = new Color(1, 1, 1, .10f);
            colors.pressedColor = new Color(.23f, .12f, .05f, .22f);
            colors.disabledColor = new Color(.25f, .22f, .17f, .43f);
            button.colors = colors;
            button.interactable = session.Config.quests.quests.Any(q => q.category == category && session.QuestAvailable(q.id, out _));
        }
        void QuestList()
        {
            Heading(QuestCatalog.CategoryName(SelectedCategory),QuestDescriptions[(int)SelectedCategory]);
            var list=ui.Scroll(content,0,100,656,700);
            var entries=session.Config.quests.quests.Where(q=>q.category==SelectedCategory).OrderBy(q=>q.order).ThenBy(q=>q.difficulty);
            foreach(var group in entries.GroupBy(q=>SelectedCategory==QuestCategory.Showdown?q.groupId:q.id))
            {
                var q=group.FirstOrDefault(v=>session.QuestAvailable(v.id,out _))??group.First();bool available=session.QuestAvailable(q.id,out string reason);
                var row=ui.Row(list,155);
                var card=ui.Button(row,"",0,0,640,155,()=>OpenQuest(q.id),available).transform;
                ui.Label(card,q.title,22,100,586,40,27);
                string status=SelectedCategory==QuestCategory.Showdown?"易・普通・難　/　スピリター獲得":
                    $"エネルギー {q.energyCost}　EXP {q.experience}"+(!q.useMasterRewards?$"　メダル {q.medals}":"");
                ui.Label(card,status,22,60,586,34,19,HubWidgets.Muted);
                ui.Label(card,available?(session.IsCleared(q.id)?"CLEAR　もう一度挑戦　→":"挑戦する　→"):reason,22,12,586,40,18,
                    available?HubWidgets.Accent:HubWidgets.Muted);
            }
            ui.Button(content,"種類選択へ",0,12,656,68,()=>Navigate(HubPage.Quests));
        }
        void QuestDetail()
        {
            var q=session.Config.quests.Find(SelectedQuestId);
            if(q==null){Navigate(HubPage.Quests);return;}
            Heading(q.title,QuestCatalog.CategoryName(q.category)+(session.IsCleared(q.id)?"　/　CLEAR":""));
            if(q.category==QuestCategory.Showdown)
            {
                int i=0;
                foreach(var variant in session.Config.quests.quests.Where(x=>x.groupId==q.groupId&&x.category==QuestCategory.Showdown).OrderBy(x=>x.difficulty))
                {
                    string id=variant.id;
                    ui.Button(content,variant.DifficultyLabel+(session.IsCleared(id)?"　済":""),i++*222,735,210,65,()=>OpenQuest(id),true,
                        q.id==id?HubWidgets.Accent:HubWidgets.PanelColor);
                }
            }
            else ui.Label(content,q.description,22,720,612,88,22,HubWidgets.Muted);
            var card=ui.Panel(content,"Quest summary",12,285,632,420,HubWidgets.PanelColor);
            string enemyLevels=q.preserveEnemyLevels&&q.enemyDeck!=null?
                (q.enemyDeck.Min(s=>s.level)==q.enemyDeck.Max(s=>s.level)?q.enemyDeck[0].level.ToString():
                    q.enemyDeck.Min(s=>s.level)+"〜"+q.enemyDeck.Max(s=>s.level)):q.enemyLevel.ToString();
            ui.Label(card,$"{q.DifficultyLabel}　敵の主属性：{elementNames[(int)q.enemyElement]}　Lv{enemyLevels}",22,355,q.enemyPortrait!=null?500:588,50,25);
            if(q.useEnemyPortrait)ui.Portrait(card,q.enemyPortrait,526,350,66,66);
            ui.Label(card,$"消費エネルギー {q.energyCost}　（現在 {session.Energy}/{session.MaximumEnergy}）",22,300,588,48,24);
            string reward=$"クリア報酬\nEXP {q.experience}　メダル {q.medals:N0}";
            if(q.materialCount>0)reward+=$"\n{elementNames[q.materialType]}パワー ×{q.materialCount}";
            if(q.eventPoints>0)reward+=$"\nイベントポイント {q.eventPoints}";
            if(q.gems>0 || q.shiningStones>0)reward+=$"\n宝石 {q.gems} / 輝く石 {q.shiningStones}";
            if(q.firstClearGems>0)reward+=$"\n初回：宝石 {q.firstClearGems}"+(session.IsCleared(q.id)?"（獲得済み）":"");
            if(q.rewardSpiritIndex>=0)reward+=$"\n{session.Config.catalog[q.rewardSpiritIndex].displayName}　{q.spiritDropChance*100:0}%"+
                (q.firstClearSpirit?" / 初回確定":"");
            if(q.useMasterRewards)
            {
                reward=$"クリア報酬\nEXP {q.experience}";
                foreach(var entry in q.masterRewards)
                {
                    string name=entry.spiritIndex>=0?session.Config.catalog[entry.spiritIndex].displayName:PlayerSession.QuestRewardName(entry.itemKind);
                    string count=entry.min==entry.max?entry.min.ToString():entry.min+"〜"+entry.max;
                    reward+=$"\n{name} ×{count}"+(entry.probability<1?$"　{entry.probability*100:0.#}%":"");
                }
                if(q.firstClearGems>0)reward+=$"\n初回：宝石 {q.firstClearGems}"+(session.IsCleared(q.id)?"（獲得済み）":"");
                if(q.firstClearSpirit)reward+="\n初回スピリター1体確定（重複なし）";
            }
            ui.Label(card,reward,22,18,588,272,22);
            ui.Label(content,$"マイデッキ {session.ActiveDeck+1}　HP {session.DeckHp:N0}",22,220,612,52,25,null,true);
            if(q.IsPractice)ui.Label(content,"HP0後も練習可能。リタイアでクリア。",0,170,656,42,19,HubWidgets.Muted,true);
            else ui.Label(content,"敵・報酬の数値は調整中です",0,170,656,42,18,HubWidgets.Muted,true);
            bool open=session.QuestAvailable(q.id,out string reason);
            ui.Button(content,"戻る",0,65,190,78,()=>OpenQuestCategory(q.category));
            ui.Button(content,"出撃する",208,65,448,78,()=>RequestQuest(q.id),open,HubWidgets.Accent);
            if(!open)Message(reason);
        }
        public void RequestQuest(string id)
        {
            if(!session.QuestAvailable(id,out string reason)){Render();Message(reason);return;}
            var q=session.Config.quests.Find(id);
            if(q.energyCost>session.MaximumEnergy){Message("ランクを上げて最大エネルギーを増やしてください。");return;}
            if(session.Energy<q.energyCost){RequestEnergyRefill(()=>RequestQuest(id));return;}
            Confirm("出撃しますか？",$"{q.DisplayTitle}\nエネルギー {q.energyCost} を使用します。",()=>QuestRequested?.Invoke(id));
        }
        public void RequestEnergyRefill(Action after=null)
        {
            if(session.Energy>=session.MaximumEnergy){Message("エネルギーは満タンです。");return;}
            Confirm("エネルギーを回復しますか？",$"宝石 {session.Config.energyRefillGems}個で {session.MaximumEnergy} まで回復\n所持宝石 {session.Gems}個",()=>{
                if(!session.TryRefillEnergy(out string reason)){Render();Message(reason);return;}
                Render();Message("エネルギーが回復しました。");after?.Invoke();
            });
        }
    }
}
