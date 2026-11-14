using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ohajiki.Engine;

internal static class SaveTests
{
    static readonly DateTimeOffset InitialDay = new(2026,11,14,12,0,0,TimeSpan.FromHours(9));
    static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Rejected(Action action,string message)
    {try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or NotSupportedException or JsonException){return;}throw new InvalidOperationException(message);}
    static void Fund(PlayerSession session)
    {Assert(session.ApplyShop(ShopCurrency.SimulatedYen,0,[new(){kind=ShopRewardKind.Gems,amount=1000},new(){kind=ShopRewardKind.ShiningStones,amount=17},new(){kind=ShopRewardKind.EventPoints,amount=13}],out _),"Fixture funding");}
    static void Populate(PlayerSession session,MasterGameData data)
    {
        Fund(session);
        for(int i=0;i<data.home.deckSlots;i++){var deck=session.DeckCopy();Array.Reverse(deck);Assert(session.SaveDeck(deck),"Save deck");session.SwitchDeck(1);}session.SwitchDeck(2);
        Assert(session.Upgrade(0,(int)data.home.catalog[0].element,1),"Upgrade");session.AwardBattleExperience(session.DeckCopy(),37);
        Assert(session.TryDraw(0,1,out _,out _)&&session.TryDraw(0,10,out _,out _),"Draw");
        foreach(string product in new[]{"power_0","premium"})Assert(session.Shop.Confirm(session.Shop.Prepare(product,out _),out _),"Purchase "+product);
        var quest=data.home.quests.quests.First(q=>q.category==QuestCategory.Main&&session.QuestAvailable(q.id,out _));
        Assert(session.TryBeginQuest(quest.id,out var run,out _),"Quest start");Assert(session.CompleteQuest(run,true,data.battle).FirstClear,"First clear");
    }
    static GameApplication App(EngineProject project,string directory,Func<DateTimeOffset> clock)=>new(project,new SceneData(),new InputState(),_=>{},clock,directory);
    static void Click(GameApplication app,string id)=>Assert(app.Activate(id),"Missing button "+id+" in "+app.Screen);

    public static int Probe(string root,string directory,string phase)
    {
        // A separate OS process verifies there is no dependency on static/session memory.
        var project=new EngineProject(root);
        using var app=App(project,directory,()=>InitialDay);
        if(phase=="write")
        {
            Assert(!app.HasSavedProgress,"Fixture already exists");Populate(app.Session,app.Data);app.Update(0);
            Assert(!app.SaveBlocked,"Autosave failed");File.WriteAllText(Path.Combine(directory,"expected.json"),app.Session.CaptureSave().Encode());
        }
        else
        {
            Assert(app.HasSavedProgress,"Restart did not load");Assert(app.Session.CaptureSave().Encode()==File.ReadAllText(Path.Combine(directory,"expected.json")),"Restart progress differs");
            Click(app,"title.start");Assert(app.Screen==GameScreen.Home,"Restart route");Assert(app.Session.FirstSingleUsed(0),"First discount reset");
        }
        return 0;
    }
    public static int Run(string root,bool graphics=false)
    {
        var project=new EngineProject(root);string review=project.Resolve("Review/Save");Directory.CreateDirectory(review);
        string report=Path.Combine(review,"test-results.txt"),fixture=Path.Combine(review,"run-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));Directory.CreateDirectory(fixture);
        File.WriteAllText(report,"Persistent progress — "+DateTimeOffset.Now+"\n");int passed=0,failed=0;
        void Test(string name,Action test){try{test();passed++;File.AppendAllText(report,"PASS "+name+"\n");}catch(Exception ex){failed++;File.AppendAllText(report,"FAIL "+name+"\n"+ex+"\n");}}
        var data=GameData.Load(project);DateTimeOffset now=InitialDay;
        PlayerSession Session()=>new(data.home,data.battle,()=>now);
        string Dir(string name)=>Path.Combine(fixture,name);
        PlayerSession Reload(string directory)
        {using var store=new PlayerSaveStore(directory);var session=Session();session.RestoreSave(store.Load()!,data.battle);return session;}

        Test("All decks, training, ownership, wallets, rank, quest clears and usage survive a real process restart",()=>
        {
            foreach(string phase in new[]{"write","read"})
            {
                var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};
                foreach(string argument in new[]{typeof(SaveTests).Assembly.Location,"--project",root,"--save-probe",Dir("process"),phase})start.ArgumentList.Add(argument);
                using var child=Process.Start(start)!;if(!child.WaitForExit(30000)){child.Kill(true);throw new TimeoutException("Restart probe");}
                Assert(child.ExitCode==0,"Process "+phase+" failed; see Review/startup-error.txt");
            }
        });
        Test("Purchase and gacha confirm autosave before animation; restart never grants them twice",()=>
        {
            string expected;
            using(var app=App(project,Dir("ui"),()=>now))
            {
                Click(app,"title.start");Click(app,"nav.shop");Click(app,"shop.purchase");Click(app,"shop.tab.2");Click(app,"shop.product.gems_80");Click(app,"modal.confirm");
                Click(app,"nav.gacha");Click(app,"gacha.banner.0");Click(app,"gacha.draw.1");Click(app,"modal.confirm");
                Assert(app.Screen==GameScreen.Summon,"Animation route");expected=app.Session.CaptureSave().Encode();
                Assert(File.ReadAllText(app.SavePath!).Contains("gacha",StringComparison.OrdinalIgnoreCase),"Not saved during animation");
            }
            using var restart=App(project,Dir("ui"),()=>now);Assert(restart.Session.CaptureSave().Encode()==expected,"Duplicate or missing award");
            Assert(restart.Session.GachaCost(0,1)>Session().GachaCost(0,1),"First discount restored incorrectly");
        });
        Test("Unconfirmed purchase and deck draft never spend or replace saved progress",()=>
        {
            string expected;
            using(var app=App(project,Dir("draft"),()=>now))
            {
                Click(app,"title.start");expected=app.Session.CaptureSave().Encode();Click(app,"nav.spirits");Click(app,"deck.edit");Click(app,"collection."+app.Session.DeckCopy()[0]);Click(app,"back");
                Click(app,"nav.shop");Click(app,"shop.purchase");Click(app,"shop.tab.2");Click(app,"shop.product.gems_80");Assert(app.ModalOpen,"Quote missing");
            }
            Assert(Reload(Dir("draft")).CaptureSave().Encode()==expected,"Unconfirmed draft committed");
        });
        Test("Interrupted battle keeps spent energy, restores home, and cannot repeat first-clear rewards",()=>
        {
            int energy;string questId;
            using(var app=App(project,Dir("quest"),()=>now))
            {
                var s=app.Session;var quest=app.Data.home.quests.quests.First(q=>q.category==QuestCategory.Main&&s.QuestAvailable(q.id,out _));questId=quest.id;
                Assert(s.TryBeginQuest(questId,out var run,out _),"Begin");Assert(s.CompleteQuest(run,true,app.Data.battle).FirstClear,"First clear");
                Assert(s.TryBeginQuest(questId,out _,out _),"Second run");energy=s.Energy;app.Update(0);
            }
            using var restart=App(project,Dir("quest"),()=>now);var restored=restart.Session;
            Assert(restored.ActiveQuestRun==null&&restored.Energy==energy&&restored.IsCleared(questId),"Battle persistence");Click(restart,"title.start");Assert(restart.Screen==GameScreen.Home,"Resume home");
            Assert(restored.TryBeginQuest(questId,out var next,out _),"Replay");Assert(!restored.CompleteQuest(next,true,restart.Data.battle).FirstClear,"First clear granted twice");
        });
        Test("Premium daily claim is once per day across restarts, monthly entitlement expires",()=>
        {
            now=InitialDay;int gems;
            using(var app=App(project,Dir("premium"),()=>now)){Assert(app.Session.Shop.Confirm(app.Session.Shop.Prepare("premium",out _),out _),"Premium");app.Update(0);gems=app.Session.Gems;}
            using(var app=App(project,Dir("premium"),()=>now))Assert(app.Session.Gems==gems&&!app.Session.Shop.ClaimDailyBonus(),"Same-day double claim");
            now=now.AddDays(1);using(var app=App(project,Dir("premium"),()=>now))Assert(app.Session.Gems==gems+data.home.shop.premiumDailyGems,"Next-day bonus missing");
            using(var app=App(project,Dir("premium"),()=>now))Assert(app.Session.Gems==gems+data.home.shop.premiumDailyGems,"Next-day double claim");
            now=now.AddMonths(1);using(var app=App(project,Dir("premium"),()=>now))Assert(!app.Session.Shop.PremiumActive&&app.Session.Shop.Remaining(app.Session.Shop.Find("premium"))==1,"Monthly renewal");now=InitialDay;
        });
        Test("Daily and non-reset shop limits remain enforced after load",()=>
        {
            var product=data.home.shop.products.First(p=>p.id=="power_0");var period=product.limitPeriod;int limit=product.purchaseLimit;
            try
            {
                foreach(var mode in new[]{ShopLimitPeriod.Daily,ShopLimitPeriod.Session})
                {
                    product.limitPeriod=mode;product.purchaseLimit=1;now=InitialDay;var session=Session();Assert(session.Shop.Confirm(session.Shop.Prepare(product.id,out _),out _),"Limited purchase");
                    using(var store=new PlayerSaveStore(Dir("limits-"+mode)))store.Save(session.CaptureSave());var resumed=Reload(Dir("limits-"+mode));
                    Assert(resumed.Shop.Remaining(product)==0&&!resumed.Shop.CanPurchase(product,out _),"Limit reset on restart");now=now.AddDays(1);
                    Assert(resumed.Shop.Remaining(product)==(mode==ShopLimitPeriod.Daily?1:0),"Period reset wrong");
                }
            }
            finally{product.limitPeriod=period;product.purchaseLimit=limit;now=InitialDay;}
        });
        Test("Gacha usage counts include unlimited draws; daily limits reset only on the next day",()=>
        {
            var play=data.home.banners[0].plays.First(p=>p.drawCount==1&&p.purchaseLimit>0);int period=play.resetPeriod;
            try
            {
                play.resetPeriod=1;now=InitialDay;var session=Session();Fund(session);int discounted=session.GachaCost(0,1);
                Assert(session.TryDraw(0,1,out _,out _)&&session.TryDraw(0,1,out _,out _)&&session.TryDraw(0,10,out _,out _),"Count fixture");
                using(var store=new PlayerSaveStore(Dir("gacha-limits")))store.Save(session.CaptureSave());var resumed=Reload(Dir("gacha-limits"));
                Assert(resumed.CaptureSave().GachaUses.Values.Sum()==3,"Unlimited use not recorded");Assert(resumed.GachaCost(0,1)>discounted,"Same-day discount repeated");
                now=now.AddDays(1);Assert(resumed.GachaCost(0,1)==discounted,"Next-day discount unavailable");
            }
            finally{play.resetPeriod=period;now=InitialDay;}
        });
        Test("RuntimeWorld writes the project profile and restores it after play stops",()=>
        {
            var isolated=new EngineProject(Dir("runtime"));Directory.CreateDirectory(isolated.Resolve("Assets/Data"));
            foreach(string file in Directory.GetFiles(project.Resolve("Assets/Data")))File.Copy(file,isolated.Resolve("Assets/Data/"+Path.GetFileName(file)));
            var scene=new SceneData();scene.CreateObject("本編").AddComponent("OhajikiGame");using var compiler=new ScriptCompiler();
            using(var world=new RuntimeWorld(isolated,scene,compiler,_=>{})){Assert(world.Game!=null,"Game adapter missing");world.Game!.Session.SwitchDeck(1);world.Update(.016f);Assert(File.Exists(isolated.Resolve("SaveData/player.json")),"Runtime save missing");}
            using var restart=new RuntimeWorld(isolated,scene,compiler,_=>{});Assert(restart.Game!.Session.ActiveDeck==1&&restart.Game.HasSavedProgress,"Play restart reset progress");
        });
        Test("Master catalog reordering uses stable IDs; added entries do not regain starter gifts",()=>
        {
            var session=Session();Populate(session,data);string expected=session.CaptureSave().Encode();var original=data.home.catalog;
            try
            {
                data.home.catalog=original.Reverse().ToArray();var restored=Session();
                // Capture the source BEFORE reordering; source indices are meaningful only in its original catalog.
                restored.RestoreSave(JsonSerializer.Deserialize<PlayerSave>(expected,PlayerSave.Json)!,data.battle);
                Assert(restored.CaptureSave().Encode()==expected,"Catalog reordering changed ownership/deck");
                var added=Ohajiki.Portable.Object.Instantiate(original[0]);added.masterId="save-test-new-spirit";data.home.catalog=original.Concat([added]).ToArray();
                restored=Session();restored.RestoreSave(JsonSerializer.Deserialize<PlayerSave>(expected,PlayerSave.Json)!,data.battle);Assert(restored.Copies(original.Length)==0,"New spirit granted");
            }
            finally{data.home.catalog=original;}
        });
        Test("Random gacha sequence continues after restore",()=>
        {
            var original=Session();Fund(original);Assert(original.TryDraw(0,1,out _,out _),"Initial draw");var restored=Session();restored.RestoreSave(original.CaptureSave(),data.battle);
            for(int i=0;i<8;i++){Assert(original.TryDraw(0,10,out var a,out _),"Original continuation");Assert(restored.TryDraw(0,10,out var b,out _),"Restored continuation");Assert(a.SpiritIds.SequenceEqual(b.SpiritIds)&&a.Quantities.SequenceEqual(b.Quantities),"Random sequence restarted");}
        });
        Test("Truncated save restores previous complete backup and preserves damaged file",()=>
        {
            string directory=Dir("recover");var session=Session();string expected=session.CaptureSave().Encode();
            using(var store=new PlayerSaveStore(directory)){store.Save(session.CaptureSave());Fund(session);store.Save(session.CaptureSave());}
            string main=Path.Combine(directory,"player.json"),backup=main+".bak";string previousBackup=File.ReadAllText(backup);File.WriteAllText(main,"{\"Version\":1,");
            using(var app=App(project,directory,()=>now)){Assert(app.Session.CaptureSave().Encode()==expected,"Did not recover backup");Assert(!app.SaveBlocked,"Recovery write failed");}
            Assert(File.ReadAllText(backup)==previousBackup&&Directory.GetFiles(directory,"*.corrupt-*").Length==1,"Backup/evidence overwritten");Assert(Reload(directory).CaptureSave().Encode()==expected,"Recovered main invalid");
        });
        Test("Unreadable backups, future versions and invalid values never reset or overwrite progress",()=>
        {
            string directory=Dir("reject");using(var store=new PlayerSaveStore(directory))store.Save(Session().CaptureSave());
            string main=Path.Combine(directory,"player.json"),backup=main+".bak";string original=File.ReadAllText(main);var future=JsonNode.Parse(original)!;future["Version"]=999;File.WriteAllText(main,future.ToJsonString());string before=File.ReadAllText(main);
            Rejected(()=>{using var app=App(project,directory,()=>now);},"Future version accepted");Assert(File.ReadAllText(main)==before,"Future file overwritten");
            File.WriteAllText(main,"broken");File.WriteAllText(backup,"also broken");Rejected(()=>{using var app=App(project,directory,()=>now);},"Both corrupt accepted");Assert(File.ReadAllText(main)=="broken","Corrupt save reset");
            var session=Session();string valid=session.CaptureSave().Encode();var invalid=JsonNode.Parse(valid)!;invalid["Gems"]=-1;
            Rejected(()=>session.RestoreSave(invalid.Deserialize<PlayerSave>(PlayerSave.Json)!,data.battle),"Negative gems accepted");Assert(session.CaptureSave().Encode()==valid,"Partial restoration mutated session");
        });
        Test("Write failure keeps previous data, blocks game actions and permits safe retry",()=>
        {
            using var app=App(project,Dir("failure"),()=>now);Click(app,"title.start");string before=File.ReadAllText(app.SavePath!);
            using(var blocker=new FileStream(app.SavePath+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
            {
                app.Session.SwitchDeck(1);app.Update(0);Assert(app.SaveBlocked&&!app.Activate("nav.shop"),"Transactions not blocked");Assert(File.ReadAllText(app.SavePath!)==before,"Previous save lost");
                Assert(!app.TrySaveProgress(),"Failed save reported success");app.Update(.05f);Assert(app.Buttons.Count==1&&app.Buttons[0].Id=="save.retry","Retry control missing");
            }
            Click(app,"save.retry");Assert(!app.SaveBlocked&&app.Session.ActiveDeck==1,"Retry lost pending progress");
        });
        Test("Only one process can write a profile; unchanged frames do not rewrite saves",()=>
        {
            string directory=Dir("lock");using(var app=App(project,directory,()=>now))
            {
                Rejected(()=>{using var duplicate=new PlayerSaveStore(directory);},"Concurrent writer accepted");
                var before=File.GetLastWriteTimeUtc(app.SavePath!);string contents=File.ReadAllText(app.SavePath!);for(int i=0;i<10;i++)app.Update(.016f);
                Assert(File.GetLastWriteTimeUtc(app.SavePath!)==before&&File.ReadAllText(app.SavePath!)==contents,"Unchanged save rewritten");
            }
            using var reopened=new PlayerSaveStore(directory);Assert(reopened.Load()!=null,"Lock not released");
        });
        if(graphics)Test("D3D12 renders continue and save-error screens with no debug errors",()=>
        {
            string directory=Dir("graphics");using(var first=App(project,directory,()=>now))first.Session.SwitchDeck(1);
            using var app=App(project,directory,()=>now);using var form=new Form{ClientSize=new Size(540,960),ShowInTaskbar=false};form.Show();Application.DoEvents();
            using var renderer=new D3D12Renderer(form.Handle,540,960,true);
            void Snap(string name){app.Update(.016f);renderer.Render(SceneRenderer.Build(app.Scene,project,renderer,null,false,null,_=>{}),720,1280);using var bitmap=renderer.Capture().ToBitmap();bitmap.Save(Path.Combine(review,name+".png"),System.Drawing.Imaging.ImageFormat.Png);}
            Snap("continue");Click(app,"title.start");
            using(var blocker=new FileStream(app.SavePath+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)){app.Session.SwitchDeck(1);Snap("save-error");Assert(app.SaveBlocked,"GPU save-error fixture");}
            Click(app,"save.retry");Assert(!app.SaveBlocked&&renderer.DebugErrorCount==0,"GPU error or failed retry");form.Close();
        });
        File.AppendAllText(report,$"\n{passed} passed, {failed} failed\nFixtures: {fixture}\n");return failed==0?0:1;
    }
}
