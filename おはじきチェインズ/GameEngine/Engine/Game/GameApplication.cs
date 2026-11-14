using System.Numerics;
using PVector=Ohajiki.Portable.Vector2;

namespace Ohajiki.Engine;

public enum GameScreen { Title, Home, Spirits, Deck, Upgrade, Quests, QuestList, Battle, Result, Gacha, Banner, Summon, Shop, Settings }
public sealed record GameButton(string Id,RectangleF Bounds,Action Action,bool Enabled,RectangleF? Clip);

// Japanese game screens rendered entirely through the engine's D3D12 scene renderer.
// Clicks commit on release; confirmations, drafts and immutable receipts preserve
// the same transaction boundaries as the Unity game.
public sealed partial class GameApplication : IDisposable
{
    public MasterGameData Data {get;}
    public PlayerSession Session {get;}
    public SceneData Scene {get;}
    public GameScreen Screen {get;private set;}=GameScreen.Title;
    public GameBattle? Battle {get;private set;}
    public IReadOnlyList<GameButton> Buttons=>buttons;
    public bool ModalOpen=>modal!=null;
    public string LastNotice {get;private set;}="";
    public GachaReceipt? GachaReceipt {get;private set;}
    public int SummonIndex {get;private set;}
    public int SummonPhase {get;private set;}
    readonly PlayerSaveStore? saveStore;
    public bool SaveBlocked {get;private set;}
    public bool HasSavedProgress {get;private set;}
    public string SaveError {get;private set;}="";
    public string? SavePath=>saveStore?.FilePath;
    readonly EngineProject project;
    readonly InputState input;
    readonly Action<string> log;
    readonly List<GameButton> buttons=new();
    readonly List<int> draft=new();
    readonly List<(Vector2 Point,float Age,bool Same)> contactEffects=new();
    readonly Dictionary<string,GameObject> named=new();
    sealed record Modal(string Title,string Text,Action? Confirm,string ConfirmLabel,Action? Cancel);
    Modal? modal;
    string? pressedButton;
    bool dragging,aiming,longPressShown;
    Vector2 pressPoint,lastPointer;
    float heldTime,age,screenAge,scroll,maximumScroll,noticeAge,summonAge,hitPulseAge=1;
    int order,element=-1,rarity,sort,upgradeId=-1,upgradeMaterial,upgradeCount=1,bannerIndex,shopTab;
    QuestCategory category;
    bool shopPurchase;
    RectangleF? listClip;
    const string Ink="#3C3025",Paper="#FFF3D7",Muted="#786955",Gold="#E6B653",Red="#B94D32",Teal="#347F79";
    static readonly string[] ElementColors=["#D96B45","#4B94B6","#669660","#B7B3A4"];
    static readonly string[] ElementNames=["火","水","木","無"];
    static readonly string[] MaterialNames=["火のパワー","水のパワー","木のパワー","全のパワー"];
    public GameApplication(EngineProject project,SceneData scene,InputState input,Action<string> log,Func<DateTimeOffset>? clock=null,string? saveDirectory=null)
    {
        this.project=project;Scene=scene;this.input=input;this.log=log;
        var marker=scene.Objects.FirstOrDefault(o=>o.GetComponent("OhajikiGame")!=null)?.GetComponent("OhajikiGame");
        Data=GameData.Load(project,marker?.Get("MasterData","Assets/Data/MasterData.xlsx")??"Assets/Data/MasterData.xlsx");
        Session=new(Data.home,Data.battle,clock);log("ゲームデータ読込："+GameData.Describe(Data));
        if(saveDirectory!=null)
        {
            saveStore=new(saveDirectory);
            try
            {
                var saved=saveStore.Load();
                if(saved!=null){Session.RestoreSave(saved,Data.battle);HasSavedProgress=true;log(saveStore.RecoveredBackup?"バックアップから進行を復元しました。":"セーブデータを読み込みました。");}
                Session.Shop.RefreshCalendar();TrySaveProgress();
            }
            catch{saveStore.Dispose();throw;}
        }
        Build();
    }
    public void Update(float dt)
    {
        dt=Math.Clamp(dt,0,.05f);age+=dt;screenAge+=dt;noticeAge+=dt;
        if(SaveBlocked){HandleInput(dt);Build();return;}
        if(Session.Shop.RefreshCalendar())Build();
        HandleInput(dt);
        if(Screen==GameScreen.Battle&&Battle!=null)
        {
            Battle.Paused=modal!=null;Battle.Update(dt);
            if(Battle.Phase==BattlePhase.Finished)Go(GameScreen.Result);
        }
        if(Screen==GameScreen.Summon)
        {
            summonAge+=dt;
            float duration=SummonPhase switch {0=>MasterConstants.GetFloat("GachaSequence.approachSeconds",3.5f),1=>MasterConstants.GetFloat("GachaSequence.dropSeconds",1.25f),_=>MasterConstants.GetFloat("GachaSequence.releaseSeconds",.7f)};
            if(SummonPhase<3&&summonAge>=duration){SummonPhase++;summonAge=0;}
        }
        if(!(Screen==GameScreen.Battle&&modal!=null))
        {hitPulseAge+=dt;for(int i=contactEffects.Count-1;i>=0;i--){var p=contactEffects[i];p.Age+=dt;if(p.Age>Data.battle.contactEffectSeconds)contactEffects.RemoveAt(i);else contactEffects[i]=p;}}
        TrySaveProgress();Build();
    }
    Vector2 Logical(Vector2 p)
    {float scale=Math.Min(Scene.Width/720f,Scene.Height/1280f);return (p-new Vector2((Scene.Width-720*scale)/2,(Scene.Height-1280*scale)/2))/scale;}
    void HandleInput(float dt)
    {
        var point=Logical(input.Pointer);
        if(input.Wheel!=0&&modal==null&&listClip?.Contains(point.X,point.Y)==true)scroll=Math.Clamp(scroll-input.Wheel/120f*130,0,maximumScroll);
        if(input.Pressed)
        {
            pressPoint=lastPointer=Logical(input.PressPosition);heldTime=0;dragging=longPressShown=aiming=false;
            pressedButton=buttons.LastOrDefault(b=>b.Enabled&&b.Bounds.Contains(pressPoint.X,pressPoint.Y)&&(b.Clip==null||b.Clip.Value.Contains(pressPoint.X,pressPoint.Y)))?.Id;
            if(!SaveBlocked&&pressedButton==null&&modal==null&&Screen==GameScreen.Battle&&Battle?.Phase==BattlePhase.Ready)
            {
                var p=FromBoard(pressPoint);float line=Battle.Simulation.Position.y;
                if(Math.Abs(p.y-line)<=MasterConstants.GetFloat("PrototypeBattle.launchLineTouchHalfHeight",42)&&p.x>=0&&p.x<=ShotSimulation.Width)
                {Battle.Reposition(p.x);aiming=true;}
            }
        }
        if(input.Held)
        {
            heldTime+=dt;
            if(!SaveBlocked&&Screen==GameScreen.Battle&&modal==null&&pressedButton?.StartsWith("battle.hand.")==true&&heldTime>=Data.battle.longPressSeconds&&!longPressShown)
            {int slot=int.Parse(pressedButton.Split('.').Last());ShowSpirit(Battle!.Tuning.playerDeck[Battle.Deck.CardAt(slot)],null);longPressShown=true;pressedButton=null;}
            if(modal==null&&listClip?.Contains(pressPoint.X,pressPoint.Y)==true&&Math.Abs(point.Y-pressPoint.Y)>12)
            {dragging=true;scroll=Math.Clamp(scroll-(point.Y-lastPointer.Y),0,maximumScroll);}
            lastPointer=point;
        }
        if(input.Released)
        {
            if(!SaveBlocked&&aiming&&Battle!=null&&modal==null)
            {var delta=point-pressPoint;Battle.Shoot(new PVector(delta.X,-delta.Y));}
            else if(!dragging&&!longPressShown&&pressedButton!=null)
            {
                var button=buttons.LastOrDefault(b=>b.Id==pressedButton&&b.Enabled);
                if(button!=null&&(!SaveBlocked||button.Id=="save.retry")&&button.Bounds.Contains(point.X,point.Y)&&(button.Clip==null||button.Clip.Value.Contains(point.X,point.Y)))button.Action();
            }
            pressedButton=null;aiming=dragging=false;
        }
    }
    public bool Activate(string id)
    {if(SaveBlocked&&id!="save.retry")return false;var button=buttons.LastOrDefault(b=>b.Id==id&&b.Enabled);if(button==null)return false;button.Action();TrySaveProgress();Build();return true;}
    public bool TrySaveProgress()
    {
        if(saveStore==null)return true;
        try {saveStore.Save(Session.CaptureSave());SaveBlocked=false;SaveError="";return true;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SaveBlocked=true;SaveError=(ex.HResult&0xffff) switch
            {
                32 or 33=>"別のアプリが保存ファイルを使用しています。\nファイルを使用中のアプリを閉じてください。",
                39 or 112=>"保存先の空き容量が不足しています。",
                5=>"保存先への書き込みが許可されていません。",
                _=>"保存先に書き込めません。フォルダの状態と\n書き込み権限を確認してください。"
            };
            aiming=false;pressedButton=null;log("セーブ失敗："+ex.Message);return false;
        }
    }
    void Go(GameScreen screen)
    {Screen=screen;screenAge=scroll=maximumScroll=0;listClip=null;pressedButton=null;aiming=false;if(screen is GameScreen.Battle or GameScreen.Summon or GameScreen.Title)noticeAge=10;}
    void Notice(string text){LastNotice=text;noticeAge=0;log(text);}
    void Confirm(string title,string text,Action action,string label="決定",Action? cancel=null)
    {modal=new(title,text,action,label,cancel);pressedButton=null;}
    void Information(string title,string text){modal=new(title,text,null,"閉じる",null);pressedButton=null;}
    void Refill(Action? after=null)
    {Confirm("エネルギーを回復",$"宝石 {Data.home.energyRefillGems}個で\nエネルギーを最大まで回復します。\n\n現在 {Session.Energy} / {Session.MaximumEnergy}\n所持宝石 {Session.Gems}個",()=>{if(Session.TryRefillEnergy(out var error)){Notice("エネルギーを回復しました");after?.Invoke();}else Information("回復できません",error);},"回復する");}
    GameObject Rect(string name,float x,float y,float w,float h,string color,string shape="四角",float opacity=1,string texture="")
    {
        var obj=new GameObject{Id=name+":"+order,Name=name,Order=order++,Transform=new(){X=x+w/2,Y=y+h/2,Width=w,Height=h}};
        var c=obj.AddComponent("Sprite");c.Set("Color",color);c.Set("Shape",shape);c.Set("Opacity",opacity);c.Set("Texture",texture);Scene.Objects.Add(obj);named[name]=obj;return obj;
    }
    GameObject Label(string text,float x,float y,float w,float h,float size=26,string color=Ink,bool bold=false,string? name=null)
    {
        var obj=new GameObject{Id=(name??"text")+":"+order,Name=name??text,Order=order++,Transform=new(){X=x+w/2,Y=y+h/2,Width=w,Height=h}};
        var c=obj.AddComponent("Text");c.Set("Text",text);c.Set("FontSize",size);c.Set("Color",color);c.Set("Bold",bold);Scene.Objects.Add(obj);if(name!=null)named[name]=obj;return obj;
    }
    void Image(string path,float x,float y,float w,float h,float opacity=1,string shape="四角")=>Rect("画像",x,y,w,h,"#FFFFFF",shape,opacity,path);
    void Button(string id,string title,float x,float y,float w,float h,Action action,bool enabled=true,string color=Teal,float font=26,RectangleF? clip=null)
    {
        Rect(id,x,y+4,w,h,Ink,opacity:.17f);Rect(id,x,y,w,h,enabled?color:"#B8B1A0");
        Label(title,x+5,y+2,w-10,h-4,font,enabled?(color==Paper?Ink:"#FFFFFF"):"#676154",true);
        buttons.Add(new(id,new(x,y,w,h),action,enabled,clip));
    }
    void Portrait(SpiritDefinition spirit,float x,float y,float w,float h,float opacity=1)
    {if(spirit.portrait!=null)Image(spirit.portrait.Path,x,y,w,h,opacity);else Rect("未設定画像",x,y,w,h,ElementColors[(int)spirit.element],opacity:opacity);}
    void Back(GameScreen target,string label="戻る")=>Button("back",label,24,1150,170,64,()=>Go(target),color:Paper,font:25);
    void Header(string title)
    {
        Rect("ヘッダー",0,0,720,150,Ink);Label($"RANK {Session.Progress.Rank}",20,10,145,48,25,Gold,true);
        Label($"メダル {Session.Progress.Coins:N0}",180,8,260,48,24,Paper);Label($"宝石 {Session.Gems:N0}",450,8,230,48,25,Paper);
        Label(title,20,75,440,62,32,Paper,true);
        Label($"エネルギー {Session.Energy}/{Session.MaximumEnergy}",415,73,240,64,21,Paper);
        Button("energy", "+",660,83,42,46,()=>Refill(),color:Teal,font:26);
    }
    void Footer()
    {
        Rect("フッター",0,1216,720,64,Ink);
        (string Name,GameScreen Screen)[] links=[("ホーム",GameScreen.Home),("スピリター",GameScreen.Spirits),("ガチャ",GameScreen.Gacha),("ショップ",GameScreen.Shop),("設定",GameScreen.Settings)];
        for(int i=0;i<links.Length;i++){var link=links[i];Button("nav."+link.Screen.ToString().ToLowerInvariant(),link.Name,i*144+3,1221,138,54,()=>Go(link.Screen),color:Screen==link.Screen?Teal:Ink,font:22);}
    }
    void StartList(float top,float bottom,float contentHeight)
    {listClip=new(18,top,684,bottom-top);maximumScroll=Math.Max(0,contentHeight-(bottom-top));scroll=Math.Clamp(scroll,0,maximumScroll);}
    void EndList()
    {
        if(listClip is not {} clip)return;
        if(maximumScroll>0){float h=Math.Max(36,clip.Height*clip.Height/(clip.Height+maximumScroll));Rect("スクロール",704,clip.Top+scroll/maximumScroll*(clip.Height-h),5,h,Muted,opacity:.65f);}
    }
    void Build()
    {
        Scene.Objects.Clear();buttons.Clear();named.Clear();order=0;listClip=null;
        Scene.Name="おはじきチェインズ / "+Screen;Scene.Background=Paper;
        Image("Assets/Imported/Resources/UI/SunnyRuins.png",0,0,720,1280,.45f);
        Rect("紙地",0,0,720,1280,Paper,opacity:.78f);
        switch(Screen)
        {
            case GameScreen.Title:Title();break;
            case GameScreen.Home:Home();break;
            case GameScreen.Spirits:Spirits();break;
            case GameScreen.Deck:Collection(true);break;
            case GameScreen.Upgrade:Collection(false);break;
            case GameScreen.Quests:Quests();break;
            case GameScreen.QuestList:QuestList();break;
            case GameScreen.Battle:BattleScreen();break;
            case GameScreen.Result:ResultScreen();break;
            case GameScreen.Gacha:GachaList();break;
            case GameScreen.Banner:Banner();break;
            case GameScreen.Summon:Summon();break;
            case GameScreen.Shop:Shop();break;
            case GameScreen.Settings:Settings();break;
        }
        if(Screen is not (GameScreen.Title or GameScreen.Battle or GameScreen.Result or GameScreen.Summon))Footer();
        if(noticeAge<3&&LastNotice.Length>0&&modal==null){Rect("通知",40,1100,640,85,Ink,opacity:.95f);Label(LastNotice,54,1105,612,75,23,Paper);}
        if(modal is {} m)
        {
            buttons.Clear();listClip=null;Rect("モーダル背景",0,0,720,1280,"#221A12",opacity:.75f);
            Rect("確認枠",38,235,644,800,Gold);Rect("確認用紙",44,241,632,788,Paper);
            Label(m.Title,68,265,584,90,34,Ink,true);Label(m.Text,70,365,580,495,26,Ink);
            if(m.Confirm!=null)
            {
                Button("modal.cancel","戻る",80,910,245,72,()=>{modal=null;m.Cancel?.Invoke();},color:Muted);
                Button("modal.confirm",m.ConfirmLabel,395,910,245,72,()=>{modal=null;m.Confirm();},color:Teal);
            }
            else Button("modal.close","閉じる",160,910,400,72,()=>modal=null);
        }
        if(SaveBlocked)
        {
            buttons.Clear();listClip=null;Rect("保存エラー背景",0,0,720,1280,Ink,opacity:.92f);
            Label("保存できませんでした",55,255,610,100,38,Paper,true);
            Label("進行を保護するため一時停止しています。\n空き容量・フォルダの書き込み権限を確認して、\n保存を再試行してください。\n\n"+SaveError,55,390,610,450,24,Paper);
            Button("save.retry","保存を再試行",110,940,500,85,()=>TrySaveProgress());
        }
        float scale=Math.Min(Scene.Width/720f,Scene.Height/1280f),offsetX=(Scene.Width-720*scale)/2,offsetY=(Scene.Height-1280*scale)/2;
        if(scale!=1||offsetX!=0||offsetY!=0)foreach(var obj in Scene.Objects){obj.Transform.X=obj.Transform.X*scale+offsetX;obj.Transform.Y=obj.Transform.Y*scale+offsetY;obj.Transform.ScaleX*=scale;obj.Transform.ScaleY*=scale;}
    }
    void Title()
    {
        Image("Assets/Imported/Resources/UI/SunnyRuins.png",0,0,720,1280);
        Portrait(Data.home.catalog[0],25,230,390,585);Portrait(Data.home.catalog[1],340,420,330,495,.9f);
        Rect("タイトル帯",0,45,720,220,Ink,opacity:.91f);Label("おはじき\nチェインズ",25,65,670,180,66,Paper,true);
        Rect("開始帯",0,935,720,345,Ink,opacity:.93f);
        Button("title.start",HasSavedProgress?"つづきから":"タッチしてスタート",80,975,560,88,()=>Go(GameScreen.Home),color:Teal,font:34);
        Button("title.backup","セーブについて",155,1100,410,56,()=>Information("オートセーブ",saveStore==null?"これは保存しないプレビューです。":"進行はこの端末に自動保存されます。\n次回はホームから再開できます。\n\nデータの移動・バックアップは、ゲーム終了後にSaveDataフォルダをコピーしてください。\n\nApple ID・Google連携は準備中です。"),color:Ink,font:23);
        Label("Ver. 1.000",80,1192,560,50,20,Paper);
    }
    void DeckPreview(float top)
    {
        var deck=Session.DeckCopy();
        Button("deck.prev","〈",24,top,60,60,()=>Session.SwitchDeck(-1),color:Ink);
        Label($"マイデッキ {Session.ActiveDeck+1}   HP {Session.DeckHp:N0}",90,top,540,60,30,Ink,true);
        Button("deck.next","〉",636,top,60,60,()=>Session.SwitchDeck(1),color:Ink);
        for(int i=0;i<deck.Length;i++){int id=deck[i];SmallSpirit(id,36+i%6*110,top+80+i/6*150,100,140,()=>ShowSpirit(Data.home.catalog[id],id),"deck.preview."+i);}
    }
    void Home()
    {
        var spirit=Data.home.catalog[Session.DeckCopy()[0]];
        Portrait(spirit,243,235,234,351);Label("今日も、仲間とつなげよう。",50,166,620,60,30,Ink,true);
        Rect("ホーム編成地",16,585,688,425,Paper,opacity:.94f);DeckPreview(605);
        Button("home.battle","スピリットバトル",65,1035,590,90,()=>Go(GameScreen.Quests),font:35);
        Header("ホーム");
    }
    void Spirits()
    {
        DeckPreview(200);
        Label("12体を選んで、自分だけのデッキに。",40,660,640,70,28);
        Button("deck.edit","デッキ編成",80,770,560,94,()=>{draft.Clear();draft.AddRange(Session.DeckCopy());Go(GameScreen.Deck);},font:33);
        Button("spirit.upgrade","スピリター強化",80,900,560,94,()=>{upgradeId=-1;Go(GameScreen.Upgrade);},color:Red,font:33);
        Header("スピリター");
    }
    void SmallSpirit(int id,float x,float y,float w,float h,Action click,string buttonId,RectangleF? clip=null,bool selected=false)
    {
        var s=Data.home.catalog[id];Rect("カード縁",x,y,w,h,selected?Gold:ElementColors[(int)s.element]);Rect("カード紙",x+3,y+3,w-6,h-6,Paper);
        float artH=h-48;Portrait(s,x+(w-artH*2/3)/2,y+2,artH*2/3,artH);
        Label($"{ElementNames[(int)s.element]} ★{s.rarity} Lv{Session.Level(id)}",x+2,y+h-49,w-4,24,15,Ink,true);
        Label(s.displayName,x+3,y+h-27,w-6,25,17);
        if(selected){Rect("選択印",x+w-29,y+4,25,25,Teal);Label("✓",x+w-29,y+3,25,25,22,"#FFFFFF",true);}
        buttons.Add(new(buttonId,new(x,y,w,h),click,true,clip));
    }
    void Collection(bool editing)
    {
        if(!editing&&upgradeId>=0){Upgrade();return;}
        var ids=Session.FindSpirits(element,rarity,sort);
        StartList(350,1075,MathF.Ceiling(ids.Length/4f)*214);
        for(int j=0;j<ids.Length;j++)
        {
            int id=ids[j];float y=350+j/4*214-scroll;if(y+202<350||y>1075)continue;
            SmallSpirit(id,28+j%4*174,y,162,202,()=>{if(editing){if(!draft.Remove(id)){if(draft.Count<12)draft.Add(id);else Notice("12体選択中です。外すスピリターを選んでください");}}else{upgradeId=id;upgradeMaterial=(int)Data.home.catalog[id].element;upgradeCount=1;scroll=0;}},"collection."+id,listClip,editing&&draft.Contains(id));
        }
        EndList();Rect("リスト上部",0,150,720,200,Paper);Rect("リスト下部",0,1075,720,141,Paper);
        Button("filter.element","属性："+(element<0?"すべて":ElementNames[element]),24,182,220,60,()=>{element=element==2?-1:element+1;scroll=0;},color:Ink,font:23);
        Button("filter.rarity","レア："+(rarity==0?"すべて":"★"+rarity),258,182,220,60,()=>{rarity=(rarity+1)%6;scroll=0;},color:Ink,font:23);
        Button("filter.sort",new[]{"名前順","属性順","レアリティ順"}[sort],492,182,204,60,()=>{sort=(sort+1)%3;scroll=0;},color:Ink,font:23);
        Label(editing?$"マイデッキ {Session.ActiveDeck+1}   選択 {draft.Count} / 12":$"強化するスピリターを選択   所持 {ids.Length}体",24,267,672,60,28,Ink,true);
        if(ids.Length==0)Label("条件に合うスピリターがいません",40,580,640,100,28);
        Back(GameScreen.Spirits);
        if(editing)Button("deck.save","この編成で決定",245,1138,450,72,()=>Confirm("デッキ編成",$"マイデッキ {Session.ActiveDeck+1} を\n選択した12体で編成しますか？\n合計HP {draft.Sum(Session.Hp):N0}",()=>{if(Session.SaveDeck(draft)){Notice("デッキを保存しました");Go(GameScreen.Spirits);}}),draft.Count==12,font:28);
        Header(editing?"デッキ編成":"スピリター強化");
    }
    void Upgrade()
    {
        var s=Data.home.catalog[upgradeId];Portrait(s,250,170,220,330);
        Label(s.displayName,20,500,680,65,36,Ink,true);
        Label($"★{s.rarity} {ElementNames[(int)s.element]}属性  Lv.{Session.Level(upgradeId)} / {Session.MaximumSpiritLevel(upgradeId)}\nHP {Session.Hp(upgradeId)}   攻撃 {Session.Attack(upgradeId)}   速度 {Session.Speed(upgradeId):0}",36,570,648,105,26);
        for(int i=0;i<4;i++){int m=i;Button("upgrade.material."+i,$"{MaterialNames[i]}\n所持 {Session.MaterialCount(i)}",22+i*174,715,164,90,()=>upgradeMaterial=m,i==3||i==(int)s.element,upgradeMaterial==i?Teal:Ink,21);}
        Button("upgrade.minus","−",120,840,75,65,()=>upgradeCount=Math.Max(1,upgradeCount-1),color:Ink);
        Label($"使用数 {upgradeCount}個",210,840,300,65,30,Ink,true);
        Button("upgrade.plus","＋",525,840,75,65,()=>upgradeCount=Math.Min(Session.MaximumSpiritLevel(upgradeId),upgradeCount+1),color:Ink);
        Label($"必要メダル {Session.UpgradeCost(upgradeId,upgradeCount):N0}\nレベル +{upgradeCount*Session.MaterialLevelGain}",100,930,520,95,27);
        Button("upgrade.confirm","強化する",180,1040,360,76,()=>Confirm("スピリター強化",$"{s.displayName}\n{MaterialNames[upgradeMaterial]} ×{upgradeCount}\nメダル {Session.UpgradeCost(upgradeId,upgradeCount):N0}\nを消費して強化しますか？",()=>{if(Session.Upgrade(upgradeId,upgradeMaterial,upgradeCount))Notice("レベルが上がりました！");else Information("強化できません","素材・メダル・レベル上限を確認してください。");}),Session.CanUpgrade(upgradeId,upgradeMaterial,upgradeCount),Red,30);
        Button("back","戻る",24,1150,170,64,()=>{upgradeId=-1;scroll=0;},color:Paper,font:25);
        Button("upgrade.details","スキル詳細",400,1150,295,64,()=>ShowSpirit(s,upgradeId),color:Paper,font:25);Header("スピリター強化");
    }
    void ShowSpirit(SpiritDefinition s,int? id)
    {
        Information(s.displayName,$"{ElementNames[(int)s.element]}属性　★{s.rarity}　Lv.{(id.HasValue?Session.Level(id.Value):s.level)}\nHP {(id.HasValue?Session.Hp(id.Value):s.Hp)}　攻撃 {(id.HasValue?Session.Attack(id.Value):s.Attack)}\nスピード {(id.HasValue?Session.Speed(id.Value):s.Speed):0}\n\n{s.skillName}\n{SkillName(s.skill)}\nLv1〜5：{string.Join(" / ",Enumerable.Range(1,5).Select(x=>s.Power(x).ToString("0.###")))}\n\n{s.description}\n"+(s.actionSkill==ActionSkill.Trap?$"罠：{ElementNames[(int)s.trapTargetElement]}に {s.trapDamage}ダメージ / {s.trapOpponentTurns}ターン":s.actionSkill==ActionSkill.Split?$"分裂：{s.splitContactThreshold}接触で{s.splitTotalProjectiles}体":""));
    }
    static string SkillName(Skill skill)=>skill switch{Skill.Attack=>"固定ダメージ",Skill.PercentAttack=>"最大HPへの割合ダメージ",Skill.Heal=>"固定回復",Skill.PercentHeal=>"割合回復",Skill.Guard=>"固定防御",Skill.PercentGuard=>"割合防御",Skill.AttackUp=>"攻撃力アップ",Skill.SpeedUp=>"速度アップ",Skill.AttackDown=>"攻撃力ダウン",Skill.SpeedDown=>"速度ダウン",_=>"悪効果を解除"};
    void Quests()
    {
        for(int i=0;i<6;i++){var c=(QuestCategory)i;int count=Data.home.quests.quests.Count(q=>q.category==c);Button("quest.category."+i,QuestCatalog.CategoryName(c)+$"  ({count})",55,195+i*147,610,112,()=>{category=c;Go(GameScreen.QuestList);},count>0,i==5?Ink:Teal,32);}
        Back(GameScreen.Home);Header("スピリットバトル");
    }
    void QuestList()
    {
        var quests=Data.home.quests.quests.Where(q=>q.category==category).OrderBy(q=>q.order).ThenBy(q=>q.difficulty).ToArray();
        StartList(175,1125,quests.Length*190);
        for(int i=0;i<quests.Length;i++)
        {
            var q=quests[i];float y=175+i*190-scroll;if(y+174<175||y>1125)continue;
            bool open=Session.QuestAvailable(q.id,out string error);Rect("クエスト",24,y,672,174,Paper);
            Label((Session.IsCleared(q.id)?"CLEAR  ":"")+q.DisplayTitle,35,y+7,650,52,29,open?Ink:Muted,true);
            Label(open?$"消費エネルギー {q.energyCost}  /  勝利EXP {q.experience}\n{(q.IsPractice?"敵HPがゼロになったらリタイアでクリア":q.description)}":error,45,y+62,470,95,21,Muted);
            Button("quest."+q.id,open?"出撃":"未解放",535,y+80,140,66,()=>QuestConfirm(q),open,Teal,25,listClip);
        }
        EndList();Rect("クエスト下部",0,1125,720,91,Paper);Back(GameScreen.Quests);Header(QuestCatalog.CategoryName(category));
    }
    void QuestConfirm(QuestDefinition q)
    {
        Confirm(q.DisplayTitle,$"マイデッキ {Session.ActiveDeck+1} / HP {Session.DeckHp:N0}\n消費エネルギー {q.energyCost}\n\n敵HP {q.enemyDeck.Sum(s=>s.Hp):N0}\n勝利EXP {q.experience}\n\n{q.description}",()=>BeginQuest(q.id),"出撃する");
    }
    public bool BeginQuest(string id)
    {
        if(!Session.TryBeginQuest(id,out var run,out var reason))
        {
            if(reason=="エネルギーが不足しています")Refill(()=>BeginQuest(id));else Information("出撃できません",reason);return false;
        }
        try
        {
            Battle?.Dispose();Battle=new(Session,Data.battle,run);Battle.Contact+=(p,same)=>{contactEffects.Add((ToBoard(p),0,same));hitPulseAge=0;};contactEffects.Clear();Go(GameScreen.Battle);return true;
        }
        catch{Session.AbandonQuest(run);throw;}
    }
    void Settings()
    {
        Label("遊び方",40,200,640,70,40,Ink,true);
        Label("① 12体でマイデッキを編成\n② クエストを選んで出撃\n③ 手スピを選び、発射位置から上へスワイプ\n④ 同属性の接触でスキル効果度アップ\n⑤ 手スピ長押しでスキル詳細\n\n一覧はホイール・上下ドラッグでスクロール\n一時停止はバトル画面右上の「停止」",40,300,640,410,27);
        Label("進行は自動保存され、次回も続けて遊べます。\n戦闘中に終了した場合はホームから再開します。\nテスト購入に実際の請求は発生しません。\nクラウド連携・戦友・マルチは未実装です。",40,770,640,210,25,Muted);
        Button("settings.title","タイトルへ",100,1030,520,80,()=>Go(GameScreen.Title),color:Ink,font:30);Header("設定・操作ガイド");
    }
    bool disposed;
    public void Dispose()
    {if(disposed)return;disposed=true;try{TrySaveProgress();Battle?.Dispose();}finally{saveStore?.Dispose();}}
}
