using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Ohajiki
{
    public enum HubPage { Home, Spirits, Deck, UpgradeList, UpgradeDetail, Materials, Gacha, Banner, Shop, Settings, Quests, QuestList, QuestDetail }
    public sealed partial class HubView : MonoBehaviour
    {
        public event Action BattleRequested;
        public event Action<string> QuestRequested;
        public event Action TitleRequested;
        public event Action ResetRequested;
        public HubPage Page { get; private set; }
        public bool HasModal => modal != null;
        public string LastMessage { get; private set; }
        PlayerSession session;
        BattleTuning battleTuning;
        HubWidgets ui;
        SpiritUiSkin skin;
        Canvas canvas;
        CanvasGroup interaction;
        Action modalCancelled;
        float calendarTimer;
        RectTransform screen,content,modal;
        TMP_Text account,resources,toast;
        UnityEngine.UI.Button[] navigation;
        RectTransform[] navigationIcons;
        List<int> draft;
        int elementFilter=-1,rarityFilter,sort,selectedSpirit,selectedMaterial,quantity=1,selectedBanner;
        int lastWidth,lastHeight;
        readonly string[] elementNames={"火","水","木","全"};
        public void Initialize(PlayerSession player,BattleTuning tuning,TMP_FontAsset font)
        {
            session=player; battleTuning=tuning;
            skin=gameObject.AddComponent<SpiritUiSkin>();skin.Initialize();ui=new HubWidgets(font,skin);
            canvas=gameObject.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            new GameObject("HubEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)).transform.SetParent(transform);
            screen=ui.Panel(transform,"HomeScreen",0,0,720,1280,new Color(.55f,.86f,1));
            interaction=screen.gameObject.AddComponent<CanvasGroup>();
            screen.anchorMin=screen.anchorMax=screen.pivot=new Vector2(.5f,.5f); screen.anchoredPosition=Vector2.zero;
            skin.Backdrop(screen,false);
            ui.Panel(screen,"AdventureHeader",24,1127,672,131,new Color(1,.98f,.86f,.94f));
            ui.Label(screen,"<color=#ED5518>おはじき</color><color=#127890>チェインズ</color>",38,1212,500,56,29);
            ui.Label(screen,"ADVENTURE",522,1226,152,28,16,HubWidgets.Elements[1],true);
            account=ui.Label(screen,"",32,1187,656,38,25);
            resources=ui.Label(screen,"",32,1136,596,40,19);
            resources.enableAutoSizing=true;resources.fontSizeMin=14;resources.fontSizeMax=19;
            ui.Button(screen,"＋",634,1138,44,38,()=>RequestEnergyRefill()).name="Button_EnergyRefill";
            content=ui.Rect(screen,"PageContent",32,160,656,940);
            toast=ui.Label(screen,"",32,118,656,40,17,HubWidgets.Ink,true);
            navigation=new UnityEngine.UI.Button[5];
            navigationIcons=new RectTransform[5];
            string[] names={"ホーム","スピリター","ガチャ","ショップ","設定"};
            HubPage[] routes={HubPage.Home,HubPage.Spirits,HubPage.Gacha,HubPage.Shop,HubPage.Settings};
            for(int i=0;i<5;i++)
            {
                int slot=i;
                navigation[i]=ui.Button(screen,names[i],32+i*132,25,124,88,
                    ()=>{if(slot<routes.Length)Navigate(routes[slot]);},true);
                var navLabel=navigation[i].GetComponentInChildren<TMP_Text>();navLabel.fontSize=18;
                navLabel.rectTransform.anchoredPosition=new Vector2(6,4);navLabel.rectTransform.sizeDelta=new Vector2(112,30);
                navigationIcons[i]=ui.Rect(navigation[i].transform,"Navigation art",0,3,124,76);
                skin.NavigationIcon(navigationIcons[i],i,HubWidgets.Accent);
            }
            ui.Label(screen,"試作版 / 進行状況はこの端末に自動保存されます",32,3,656,20,12,HubWidgets.Muted,true);
            Navigate(HubPage.Home); Resize();
            if(!session.HasPlayerName)EditProfileText(true,true);
        }
        void Update()
        {
            Resize();calendarTimer-=Time.unscaledDeltaTime;
            UpdateFeatured();
            if(calendarTimer<=0)
            {
                calendarTimer=1;
                if(session.RefreshEnergy())RefreshHeader();
                if(session.Shop!=null && session.Shop.RefreshCalendar() && !HasModal)Render();
            }
        }
        void Resize()
        {
            if(lastWidth==Screen.width && lastHeight==Screen.height)return;
            lastWidth=Screen.width; lastHeight=Screen.height;
            canvas.scaleFactor=Mathf.Min(Screen.width/720f,Screen.height/1280f);
        }
        public void Navigate(HubPage destination)
        {
            if(!session.HasPlayerName && HasModal)return;
            if(GachaMovie!=null)return;
            if(Page==HubPage.Deck && draft!=null && !draft.SequenceEqual(session.DeckCopy()) && destination!=HubPage.Deck)
            {
                Confirm("編集中の変更を破棄しますか？","確定していない編成は保存されません。",()=>{draft=null; Open(destination);}); return;
            }
            Open(destination);
        }
        void Open(HubPage destination)
        {
            CloseModal(); Page=destination;
            if(Page==HubPage.Deck) { draft=session.DeckCopy().ToList(); elementFilter=-1; rarityFilter=0; sort=0; }
            Render();
        }
        public void Refresh() => Render();
        public void Message(string value) { LastMessage=value; toast.text=value; }
        void Clear(Transform parent)
        {
            foreach(Transform child in parent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        }
        void Render()
        {
            if(GachaMovie!=null)return;
            Clear(content); CloseModal();
            if(Page!=HubPage.Home)ui.Panel(content,"AdventurePage",0,0,656,940,new Color(1,.98f,.88f,.96f));
            session.Shop?.RefreshCalendar();
            RefreshHeader();
            int active=Page==HubPage.Settings?4:(Page==HubPage.Home||Page>=HubPage.Quests)?0:Page==HubPage.Shop?3:(Page==HubPage.Gacha||Page==HubPage.Banner)?2:1;
            for(int i=0;i<navigation.Length;i++)
            {
                bool selected=i==active;Color fill=selected?HubWidgets.Accent:HubWidgets.PanelColor;
                navigation[i].GetComponent<UnityEngine.UI.Image>().color=fill;
                navigation[i].GetComponentInChildren<TMP_Text>().color=selected?Color.white:HubWidgets.Ink;
                foreach(var icon in navigationIcons[i].GetComponentsInChildren<UnityEngine.UI.Image>())
                    icon.color=icon.name=="Door"||icon.name=="Bag star"||icon.name=="Cog core"?fill:selected?Color.white:HubWidgets.Accent;
            }
            Message("");
            switch(Page)
            {
                case HubPage.Home: Home(); break;
                case HubPage.Spirits: Spirits(); break;
                case HubPage.Deck: EditDeck(); break;
                case HubPage.UpgradeList: UpgradeList(); break;
                case HubPage.UpgradeDetail: UpgradeDetail(); break;
                case HubPage.Materials: Materials(); break;
                case HubPage.Gacha: Gacha(); break;
                case HubPage.Banner: BannerDetail(); break;
                case HubPage.Shop: ShopPage(); break;
                case HubPage.Settings: SettingsPage(); break;
                case HubPage.Quests: QuestMenu(); break;
                case HubPage.QuestList: QuestList(); break;
                case HubPage.QuestDetail: QuestDetail(); break;
            }
        }
        void Heading(string title,string subtitle)
        {
            ui.Label(content,title,22,864,612,74,36);ui.Label(content,subtitle,22,831,612,40,19,HubWidgets.Muted);
            skin.Image(content,"Heading gem",skin.Shard,630,904,14,24,SpiritUiSkin.Gold,true);
        }
        void RefreshHeader()
        {
            account.text=$"Rank {session.Progress.Rank}　EXP {session.Progress.RankExperience}/{session.Progress.Needed(battleTuning)}";
            resources.text=$"{PlayerSession.QuestRewardName(ShopRewardKind.Medals)} {session.Progress.Coins:N0}　宝石 {session.Gems:N0}　エネルギー {session.Energy}/{session.MaximumEnergy}";
        }
        void DeckSwitcher(float y)
        {
            ui.Button(content,"＜",0,y,60,52,()=>{session.SwitchDeck(-1);Render();});
            ui.Label(content,$"マイデッキ {session.ActiveDeck+1}  /  {session.Config.deckSlots}",70,y,516,52,24,null,true);
            ui.Button(content,"＞",596,y,60,52,()=>{session.SwitchDeck(1);Render();});
        }
        void DeckPreview(float y)
        {
            int[] deck=session.DeckCopy();
            for(int i=0;i<deck.Length;i++)
            {
                int id=deck[i]; var spirit=session.Config.catalog[id];
                var card=ui.Panel(content,"DeckSlot",i%6*110,y-i/6*125,104,114,Color.Lerp(HubWidgets.PanelColor,HubWidgets.Elements[(int)spirit.element],.12f));
                skin.Image(card,"Element jewel",skin.Ball,14,99,12,12,HubWidgets.Elements[(int)spirit.element],true);
                ui.Portrait(card,spirit.portrait,15,27,74,84);
                ui.Label(card,$"{elementNames[(int)spirit.element]} Lv{session.Level(id)}",2,2,100,25,16,HubWidgets.Elements[(int)spirit.element],true);
            }
        }
        void Home()
        {
            Heading("冒険の準備","はじいて、つないで、遺跡を冒険！");
            int hero=session.Favorite(favoritePage);if(hero<0){favoritePage=0;hero=session.Favorite(0);}
            var spirit=session.Config.catalog[hero];
            var feature=ui.Panel(content,"FeaturedSpirit",0,489,656,322,new Color(1,.97f,.80f,.92f));
            skin.Halo(feature,new Vector2(501,157),264,Color.Lerp(HubWidgets.Elements[(int)spirit.element],Color.white,.25f));
            ui.Portrait(feature,spirit.portrait,350,5,290,310);
            ui.Label(feature,"お気に入り "+(favoritePage+1),24,255,330,36,18,HubWidgets.Elements[(int)spirit.element]);
            ui.Label(feature,spirit.displayName,24,190,335,60,38);
            ui.Label(feature,$"{elementNames[(int)spirit.element]}属性　Lv{session.Level(hero)}\nデッキHP {session.DeckHp:N0}",24,100,330,80,24);
            ui.Label(feature,"さあ、チェインをつなごう！",24,22,330,65,21,HubWidgets.Muted);
            ui.Button(content,"＜",0,422,60,52,()=>CycleFavorite(-1));
            ui.Button(content,"お気に入りを変更",76,422,504,52,ShowProfile);
            ui.Button(content,"＞",596,422,60,52,()=>CycleFavorite(1));
            ui.Button(content,"お知らせ",0,344,320,56,ShowNews);
            ui.Button(content,"プレゼントBOX",336,344,320,56,()=>ShowInformation("プレゼントBOX","受け取れるプレゼントはありません。"));
            ui.Label(content,$"マイデッキ {session.ActiveDeck+1}　HP {session.DeckHp:N0}",0,252,656,48,25,null,true);
            ui.Button(content,"デッキ編成",160,180,336,56,()=>Navigate(HubPage.Deck));
            ui.Button(content,"スピリットバトル　→",0,88,656,70,()=>{
                if(session.Config.quests!=null)Navigate(HubPage.Quests);else BattleRequested?.Invoke();
            },true,HubWidgets.Accent);
            ui.Button(content,"マルチ（準備中）",0,20,320,50,null,false);
            ui.Button(content,"ミッション（準備中）",336,20,320,50,null,false);
        }
        void Spirits()
        {
            Heading("スピリター","編成と強化で、自分のデッキを整えよう。");
            DeckSwitcher(747); DeckPreview(610);
            ui.Label(content,$"選択中のデッキ　12体 / HP {session.DeckHp:N0}",0,354,656,46,25,null,true);
            ui.Button(content,"デッキ編成　→",0,242,656,84,()=>Navigate(HubPage.Deck),true,HubWidgets.Accent);
            ui.Button(content,"スピリター強化　→",0,126,656,84,()=>Navigate(HubPage.UpgradeList));
        }
        void Filters()
        {
            string element=elementFilter<0?"すべて":elementNames[elementFilter];
            string rarity=rarityFilter==0?"すべて":"★"+rarityFilter;
            ui.Button(content,$"フィルター\n{element} / {rarity}",0,754,320,64,()=>OpenFilters(elementFilter,rarityFilter)).name="Button_フィルター";
            string[] sorts={"名前順","属性順","レア順"};
            ui.Button(content,"ソート\n"+sorts[sort],336,754,320,64,OpenSort).name="Button_ソート";
        }
        void OpenFilters(int selectedElement,int selectedRarity)
        {
            OpenModal("InventoryFilters",new Color(.025f,.07f,.1f,1));
            ui.Label(modal,"フィルター",48,1080,624,70,34,null,true);
            ui.Label(modal,"属性",48,982,624,45,25);
            for(int i=-1;i<3;i++)
            {
                int value=i;
                ui.Button(modal,i<0?"全属性":elementNames[i],48+(i+1)*156,894,144,68,
                    ()=>OpenFilters(value,selectedRarity),true,i==selectedElement?HubWidgets.Accent:HubWidgets.PanelColor);
            }
            ui.Label(modal,"レアリティ",48,798,624,45,25);
            for(int i=0;i<=5;i++)
            {
                int value=i;
                ui.Button(modal,i==0?"全レアリティ":"★"+i,48+(i%3)*208,704-(i/3)*90,196,72,
                    ()=>OpenFilters(selectedElement,value),true,i==selectedRarity?HubWidgets.Accent:HubWidgets.PanelColor);
            }
            ui.Button(modal,"条件をリセット",48,482,624,64,()=>OpenFilters(-1,0));
            ui.Button(modal,"戻る",48,340,288,76,CloseModal);
            ui.Button(modal,"決定",360,340,312,76,()=>{
                elementFilter=selectedElement;rarityFilter=selectedRarity;Render();
            },true,HubWidgets.Accent);
        }
        void OpenSort()
        {
            OpenModal("InventorySort",new Color(.025f,.07f,.1f,1));
            ui.Label(modal,"ソート",48,1080,624,70,34,null,true);
            string[] names={"名前順","属性順","レア順"};
            for(int i=0;i<names.Length;i++)
            {
                int value=i;
                ui.Button(modal,names[i],72,880-i*116,576,88,()=>{sort=value;Render();},
                    true,i==sort?HubWidgets.Accent:HubWidgets.PanelColor);
            }
            ui.Button(modal,"戻る",160,420,400,76,CloseModal);
        }
        void Inventory(float y,float height,Action<int> action,bool select=false)
        {
            var list=ui.Scroll(content,0,y,656,height);
            int[] ids=session.FindSpirits(elementFilter,rarityFilter,sort);
            if(ids.Length==0) { var row=ui.Row(list,100);ui.Label(row,"該当するスピリターがいません",0,0,640,100,24,HubWidgets.Muted,true);return; }
            RectTransform current=null;
            for(int i=0;i<ids.Length;i++)
            {
                if(i%3==0)current=ui.Row(list,144);
                int id=ids[i];var spirit=session.Config.catalog[id];bool chosen=select&&draft.Contains(id);
                var card=ui.Button(current,"",i%3*216,0,208,144,()=>action(id),true,chosen?new Color(.74f,.95f,.66f):HubWidgets.PanelColor).GetComponent<RectTransform>();
                ui.Portrait(card,spirit.portrait,4,35,78,104);
                var name=ui.Label(card,spirit.displayName,82,65,122,60,19);
                name.enableAutoSizing=true; name.fontSizeMin=14; name.fontSizeMax=19;
                ui.Label(card,$"{elementNames[(int)spirit.element]} Lv{session.Level(id)}",82,35,122,32,18,HubWidgets.Elements[(int)spirit.element]);
                ui.Label(card,new string('★',spirit.rarity)+(chosen?"　選択中":$"　×{session.Copies(id)}"),8,3,192,30,18,chosen?new Color(.12f,.38f,.12f):HubWidgets.Muted,true);
            }
        }
        public void ToggleDraft(int id)
        {
            if(draft.Contains(id))draft.Remove(id);
            else if(draft.Count<SpiritDeck.Size)draft.Add(id);
            else { Message("12体選択中です。入れ替えるキャラのチェックを外してください。");return; }
            Render();
        }
        void EditDeck()
        {
            Heading($"デッキ {session.ActiveDeck+1} を編成",$"12体を選んで確定　選択中 {draft.Count}/12"); Filters();
            Inventory(147,586,ToggleDraft,true);
            ui.Button(content,"戻る",0,38,200,72,()=>Navigate(HubPage.Spirits));
            ui.Button(content,$"この編成で決定　{draft.Count}/12",216,38,440,72,()=>Confirm("この編成で決定しますか？",$"マイデッキ {session.ActiveDeck+1} を更新します。",()=>{
                if(session.SaveDeck(draft)){draft=null;Open(HubPage.Spirits);Message("編成を保存しました");}
            }),draft.Count==12,HubWidgets.Accent);
        }
        void UpgradeList()
        {
            Heading("スピリター強化","強化したいスピリターを選択"); Filters();
            Inventory(125,608,id=>{selectedSpirit=id;Open(HubPage.UpgradeDetail);});
            ui.Button(content,"戻る",0,25,200,64,()=>Navigate(HubPage.Spirits));
        }
        void UpgradeDetail()
        {
            var spirit=session.Config.catalog[selectedSpirit];
            Heading("スピリター強化",spirit.displayName);
            skin.Halo(content,new Vector2(328,604),360,HubWidgets.Elements[(int)spirit.element]);
            ui.Portrait(content,spirit.portrait,138,415,380,390);
            ui.Label(content,$"{new string('★',spirit.rarity)}　{elementNames[(int)spirit.element]}属性　Lv{session.Level(selectedSpirit)} / {session.MaximumSpiritLevel(selectedSpirit)}",0,355,656,50,28,null,true);
            ui.Label(content,$"HP {session.Hp(selectedSpirit)}　攻撃 {session.Attack(selectedSpirit)}　速度 {session.Speed(selectedSpirit):0}",0,289,656,44,24,HubWidgets.Muted,true);
            long needed = session.ExperienceToNextLevel(selectedSpirit);
            ui.Label(content,needed == 0 ? "EXP MAX" : $"EXP {session.SpiritExperience(selectedSpirit)} / {needed}",
                0,244,656,34,20,HubWidgets.Muted,true);
            bool available=session.Level(selectedSpirit)<session.MaximumSpiritLevel(selectedSpirit);
            ui.Button(content,available?"強化アイテムを選択　→":"レベル上限です",0,160,656,80,()=>{selectedMaterial=(int)spirit.element;quantity=1;Open(HubPage.Materials);},available,HubWidgets.Accent);
            ui.Button(content,"戻る",0,32,200,64,()=>Navigate(HubPage.UpgradeList));
        }
        void Materials()
        {
            var spirit=session.Config.catalog[selectedSpirit];int level=session.Level(selectedSpirit);
            Heading("強化アイテム",spirit.displayName+" を強化");
            ui.Label(content,"スピリットパワー",0,754,656,40,25);
            for(int i=0;i<4;i++)
            {
                int type=i;bool allowed=i==3||i==(int)spirit.element;
                ui.Button(content,$"{PlayerSession.QuestRewardName((ShopRewardKind)((int)ShopRewardKind.FirePower+i))}\n所持 {session.MaterialCount(i)}",i*164,626,152,106,()=>{selectedMaterial=type;quantity=1;Render();},allowed,
                    i==selectedMaterial?HubWidgets.Accent:HubWidgets.PanelColor);
            }
            ui.Label(content,"使う個数",0,543,656,44,24,null,true);
            ui.Button(content,"−",112,461,92,64,()=>{quantity=Mathf.Max(1,quantity-1);Render();},quantity>1);
            ui.Label(content,quantity.ToString(),224,461,208,64,36,null,true);
            int max=Math.Min(session.MaterialCount(selectedMaterial),(session.MaximumSpiritLevel(selectedSpirit)-level)/session.MaterialLevelGain);
            ui.Button(content,"＋",452,461,92,64,()=>{quantity++;Render();},quantity<max);
            int levelGain=quantity*session.MaterialLevelGain;
            string medalName=PlayerSession.QuestRewardName(ShopRewardKind.Medals);
            string materialName=PlayerSession.QuestRewardName((ShopRewardKind)((int)ShopRewardKind.FirePower+selectedMaterial));
            ui.Label(content,$"Lv{level} → Lv{level+levelGain}\n必要{medalName} {session.UpgradeCost(selectedSpirit,quantity):N0}\nHP +{spirit.hpPerLevel*levelGain}　攻撃 +{spirit.attackPerLevel*levelGain}",0,254,656,164,25,null,true);
            bool can=session.CanUpgrade(selectedSpirit,selectedMaterial,quantity);
            ui.Button(content,"強化する",216,131,440,76,()=>Confirm("強化しますか？",$"{materialName} {quantity}個と\n{medalName} {session.UpgradeCost(selectedSpirit,quantity):N0} を使います。",()=>{
                if(session.Upgrade(selectedSpirit,selectedMaterial,quantity)){Open(HubPage.UpgradeDetail);Message("強化しました。ステータスは次のバトルに反映されます。");}
            }),can,HubWidgets.Accent);
            ui.Button(content,"戻る",0,131,200,76,()=>Navigate(HubPage.UpgradeDetail));
            ui.Label(content,$"パワー1個でLv+{session.MaterialLevelGain}",0,40,656,42,18,HubWidgets.Muted,true);
            if(!can)Message("素材・メダル・レベル上限を確認してください。");
        }
        void Gacha()
        {
            Heading("ガチャ","気になるスピリターを見つけよう。");
            var list=ui.Scroll(content,0,15,656,790);
            for(int i=0;i<session.Config.banners.Length;i++)
            {
                int index=i;var banner=session.Config.banners[i];var spirit=session.Config.catalog[banner.featuredIndex];
                var row=ui.Row(list,280);
                var card=ui.Button(row,"",0,0,640,280,()=>{selectedBanner=index;Open(HubPage.Banner);}).GetComponent<RectTransform>();
                skin.Halo(card,new Vector2(493,142),248,Color.Lerp(HubWidgets.Elements[(int)spirit.element],Color.white,.25f));
                ui.Portrait(card,string.IsNullOrEmpty(banner.masterId)?spirit.portrait:banner.bannerTexture,357,0,260,278);
                ui.Label(card,"SPIRIT SUMMON",24,225,330,30,17,HubWidgets.Elements[(int)spirit.element]);
                ui.Label(card,banner.title,24,136,330,84,31);
                ui.Label(card,banner.description,24,58,330,72,19,HubWidgets.Muted);
                ui.Label(card,session.BannerOpen(i)?"詳細を見る　→":"開催期間外",24,15,330,36,22);
            }
            Message("必要宝石数・抽選内容は各ガチャの詳細で確認できます");
        }
        void BannerDetail()
        {
            BuildBannerDetail();
        }
        void ShowRates()
        {
            OpenModal("TrialRates",new Color(.025f,.07f,.1f,1));
            ui.Label(modal,"排出率・排出内容",48,1090,624,70,32,null,true);
            ui.Label(modal,"各スピリターの排出率を表示しています",48,1020,624,55,18,HubWidgets.Muted,true);
            var list=ui.Scroll(modal,48,240,624,750);var banner=session.Config.banners[selectedBanner];var pool=banner.pool;
            if(GachaRules.UsesRarity(banner))for(int star=5;star>=1;star--)
            {var row=ui.Row(list,52);ui.Label(row,$"★{star}　{banner.rarityWeights[star-1]}%",8,0,600,52,26,HubWidgets.Ink);}
            foreach(var group in pool.GroupBy(e=>e.spiritIndex).Where(g=>GachaRules.Probability(session.Config,banner,g.Key)>0)
                .OrderByDescending(g=>session.Config.catalog[g.Key].rarity))
            {
                var row=ui.Row(list,65);
                ui.Label(row,$"{session.Config.catalog[group.Key].displayName}　{GachaRules.Probability(session.Config,banner,group.Key)*100:0.##}%",8,0,600,65,22);
            }
            ui.Button(modal,"閉じる",160,125,400,72,CloseModal);
        }
        void Confirm(string title,string body,Action yes)
        {
            OpenModal("Confirmation",new Color(0,0,0,.82f));
            var box=ui.Panel(modal,"Dialog",48,412,624,400,HubWidgets.PanelColor,true);
            ui.Label(box,title,28,278,568,78,29,null,true);
            ui.Label(box,body,28,145,568,119,23,HubWidgets.Muted,true);
            ui.Button(box,"戻る",28,38,250,76,CloseModal);
            bool handled=false;
            ui.Button(box,"決定",304,38,292,76,()=>
            {
                if(handled)return;handled=true;
                DismissModal(false);yes();
            },true,HubWidgets.Accent);
        }
        void OpenModal(string name,Color background)
        {
            CloseModal();
            bool page=background.a>.95f;
            modal=ui.Panel(screen,name,0,0,720,1280,page?SpiritUiSkin.Paper:new Color(.19f,.24f,.21f,.65f),true);
            if(page)
            {
                skin.Backdrop(modal,false);
                ui.Panel(modal,"Result parchment",32,110,656,1080,new Color(1,.97f,.84f,.95f));
            }
            modal.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups=true;
            interaction.interactable=false;
            EventSystem.current?.SetSelectedGameObject(null);
        }
        void CloseModal() => DismissModal(true);
        void DismissModal(bool cancel)
        {
            if(modal==null)return;
            var callback=modalCancelled;modalCancelled=null;
            modal.gameObject.SetActive(false);Destroy(modal.gameObject);modal=null;
            interaction.interactable=true;EventSystem.current?.SetSelectedGameObject(null);
            if(cancel)callback?.Invoke();
        }
    }
}
