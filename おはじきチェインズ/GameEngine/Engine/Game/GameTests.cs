using System.Numerics;
using System.Drawing.Imaging;
using PVector=Ohajiki.Portable.Vector2;
using Portable=Ohajiki.Portable;

namespace Ohajiki.Engine;

internal static class GameTests
{
    static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static int Run(string root,bool graphics=false)
    {
        var project=new EngineProject(root);var review=project.Resolve("Review/Migration");Directory.CreateDirectory(review);
        var report=Path.Combine(review,graphics?"game-gpu-tests.txt":"game-tests.txt");File.WriteAllText(report,"DirectX 12 game port — "+DateTimeOffset.Now+"\n");
        int passed=0,failed=0;
        void Log(string text)=>File.AppendAllText(report,text+"\n");
        void Test(string name,Action action){try{action();passed++;Log("PASS "+name);}catch(Exception ex){failed++;Log("FAIL "+name+"\n"+ex);}}
        var data=GameData.Load(project);DateTimeOffset now=new(2026,11,14,12,0,0,TimeSpan.FromHours(9));
        PlayerSession Session()=>new(data.home,data.battle,()=>now);
        void Fund(PlayerSession s,int n=1000)=>Assert(s.ApplyShop(ShopCurrency.SimulatedYen,0,[new(){kind=ShopRewardKind.Gems,amount=n}],out _),"Fixture funding failed");
        Test("Latest workbook, authored shop, NPC deck levels and source image references",()=>
        {
            Assert(data.home.catalog.Length==24&&data.npcs.Length==25&&data.home.quests.quests.Length==25&&data.home.banners.Length==4,"Master table count");
            Assert(data.home.shop.products.Length==20,"Authored shop products lost");
            Assert(data.npcs.All(n=>n.deck.Length==12)&&data.home.quests.quests.All(q=>q.preserveEnemyLevels&&q.enemyDeck.Length==12),"NPC decks lost");
            foreach(var s in data.home.catalog)if(s.portrait!=null)Assert(File.Exists(project.Resolve(s.portrait.Path)),"Image missing");
            Assert(data.sourceHash==MasterDataImporter.Hash(project.Resolve("Assets/Data/MasterData.xlsx")),"Hash mismatch");Log(GameData.Describe(data));
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("UnityEngine")),"Unity dependency loaded");
        });
        Test("Portable vector reflection and Unity-compatible ties-to-even rounding",()=>
        {
            var v=Portable.Vector2.Reflect(new(3,-4),new(0,1));Assert(v.x==3&&v.y==4,"Reflection");
            Assert(Portable.Mathf.RoundToInt(2.5f)==2&&Portable.Mathf.RoundToInt(3.5f)==4,"Round mode");
        });
        Test("High-DPI texture import preserves the complete pixel canvas",()=>
        {
            using var original=new Bitmap(100,100);original.SetResolution(300,300);using(var g=Graphics.FromImage(original))g.Clear(Color.Red);
            var pixels=TexturePixels.FromBitmap(original);
            Assert(pixels.Rgba[4*(99*100+99)]==255&&pixels.Rgba[4*(99*100+99)+3]==255,"High-DPI bitmap was scaled inside its canvas");
        });
        Test("Invalid workbook import preserves the previous playable snapshot",()=>
        {
            var isolated=new EngineProject(Path.Combine(review,"ImportFixture"));Directory.CreateDirectory(isolated.Resolve("Assets/Data"));
            foreach(var file in Directory.GetFiles(project.Resolve("Assets/Data")))File.Copy(file,isolated.Resolve("Assets/Data/"+Path.GetFileName(file)),true);
            string broken=isolated.Resolve("broken.xlsx");File.WriteAllText(broken,"invalid workbook");string before=MasterDataImporter.Hash(isolated.Resolve("Assets/Data/MasterData.xlsx"));
            bool rejected=false;try{GameData.ImportWorkbook(isolated,broken);}catch{rejected=true;}
            Assert(rejected&&before==MasterDataImporter.Hash(isolated.Resolve("Assets/Data/MasterData.xlsx")),"Failed import replaced data");
        });
        Test("Deck ownership, uniqueness, 12 slots and isolated deck copies",()=>
        {
            var s=Session();var original=s.DeckCopy();var edited=s.DeckCopy();edited[0]=edited[1];Assert(!s.SaveDeck(edited),"Duplicate accepted");
            Assert(s.DeckCopy().SequenceEqual(original),"Draft mutated live deck");Array.Reverse(original);Assert(s.SaveDeck(original)&&s.DeckCopy().SequenceEqual(original),"Valid deck rejected");
            s.SwitchDeck(-1);Assert(s.ActiveDeck==data.home.deckSlots-1,"Deck wrap");Assert(!s.SaveDeck([0]),"Short deck accepted");
        });
        Test("Upgrade cost, materials, stat growth and atomic rejection",()=>
        {
            var s=Session();int level=s.Level(0),hp=s.Hp(0),material=(int)data.home.catalog[0].element,n=s.MaterialCount(material);long coins=s.Progress.Coins,cost=s.UpgradeCost(0,1);
            Assert(s.Upgrade(0,material,1),"Upgrade rejected");Assert(s.Level(0)==level+s.MaterialLevelGain&&s.Hp(0)>hp&&s.Progress.Coins==coins-cost&&s.MaterialCount(material)==n-1,"Upgrade delta");
            coins=s.Progress.Coins;Assert(!s.Upgrade(0,(material+1)%3,1)&&s.Progress.Coins==coins,"Wrong material spent");Assert(!s.Upgrade(0,material,10000),"Cap ignored");
        });
        Test("Master gacha probabilities, first single, ten bonus and unavailable quote",()=>
        {
            var s=Session();Fund(s);var banner=data.home.banners[0];double sum=Enumerable.Range(0,data.home.catalog.Length).Sum(i=>GachaRules.Probability(data.home,banner,i));Assert(Math.Abs(sum-1)<1e-9,"Probability total");
            int before=s.Gems,cost=s.GachaCost(0,1);Assert(s.TryDraw(0,1,out var first,out _),"Single draw");Assert(s.Gems==before-cost&&first.SpiritIds.Count==1&&s.FirstSingleUsed(0),"Single commit");
            before=s.Gems;cost=s.GachaCost(0,10);Assert(s.TryDraw(0,10,out var ten,out _),"Ten draw");Assert(s.Gems==before-cost+data.home.tenBonusGems&&ten.SpiritIds.Count==10,"Ten bonus");
            now=new(2100,1,1,0,0,0,TimeSpan.Zero);before=s.Gems;Assert(!s.TryDraw(0,1,out _,out _)&&s.Gems==before,"Expired banner accepted");now=new(2026,11,14,12,0,0,TimeSpan.FromHours(9));
        });
        Test("Shop confirmation once, cancel, currency exchange and monthly premium",()=>
        {
            var s=Session();var q=s.Shop.Prepare("power_0",out _);Assert(q!=null,"Shop quote");long coins=s.Progress.Coins;int materials=s.MaterialCount(0);Assert(s.Shop.Confirm(q!,out _),"Shop commit");Assert(s.Progress.Coins==coins-500&&s.MaterialCount(0)==materials+1,"Shop delta");Assert(!s.Shop.Confirm(q!,out _),"Double commit");
            q=s.Shop.Prepare("power_1",out _);s.Shop.Cancel(q);coins=s.Progress.Coins;Assert(!s.Shop.Confirm(q,out _)&&coins==s.Progress.Coins,"Cancelled quote committed");
            q=s.Shop.Prepare("premium",out _);int gems=s.Gems;Assert(s.Shop.Confirm(q,out _),"Premium purchase");Assert(s.Gems==gems+data.home.shop.premiumDailyGems&&!s.Shop.ClaimDailyBonus(),"Daily bonus twice");
            now=now.AddDays(1);Assert(s.Shop.ClaimDailyBonus(),"Next day bonus");now=now.AddMonths(1);Assert(!s.Shop.PremiumActive&&s.Shop.CanPurchase(s.Shop.Find("premium"),out _),"Monthly renewal");now=new(2026,11,14,12,0,0,TimeSpan.FromHours(9));
        });
        Test("Quest energy, loadout cloning, reward once, rank and battle experience",()=>
        {
            var s=Session();var q=data.home.quests.quests.First(q=>s.QuestAvailable(q.id,out _)&&!q.IsPractice);int energy=s.Energy;Assert(s.TryBeginQuest(q.id,out var run,out _),"Begin quest");Assert(s.Energy==energy-q.energyCost&&!s.TryBeginQuest(q.id,out _,out _),"Energy / concurrent run");
            using var loadout=new BattleLoadout(s,data.battle,run.Quest);Assert(!ReferenceEquals(loadout.Tuning.playerDeck[0],data.home.catalog[s.DeckCopy()[0]]),"Player clone");Assert(loadout.Tuning.enemyDeck[0].level==q.enemyDeck[0].level,"NPC level changed");
            var receipt=s.CompleteQuest(run,true,data.battle);Assert(receipt.Won&&s.IsCleared(q.id),"Quest clear");long coins=s.Progress.Coins;int gems=s.Gems;Assert(s.CompleteQuest(run,true,data.battle)==null&&coins==s.Progress.Coins&&gems==s.Gems,"Double reward");Assert(s.Progress.TotalExperience==q.experience,"Quest experience");
        });
        Test("Damage stages, hand replacement, skill threshold and affinity",()=>
        {
            var b=new BattleState(data.battle);var spirit=Portable.Object.Instantiate(data.home.catalog[0]);spirit.skill=Skill.Attack;spirit.skillPower=[10,20,30,40,50];
            int hp=b.Enemy.Hp;var normal=b.ResolveNormal(false,spirit,3,3,data.battle);Assert(b.ActionPending&&b.Enemy.Hp==hp-normal.appliedNormalDamage,"Normal stage");var result=b.ResolveSkill();Assert(!b.ActionPending&&b.Enemy.Hp==hp-result.appliedDamage&&result.level==3,"Skill stage");
            var deck=new SpiritDeck(43);var hand=Enumerable.Range(0,4).Select(deck.CardAt).ToArray();deck.Replace(1);Assert(Enumerable.Range(0,4).Where(i=>i!=1).All(i=>deck.CardAt(i)==hand[i]),"Other cards replaced");Assert(BattleRules.Affinity(Element.Fire,Element.Wood,data.battle)==data.battle.advantage,"Affinity");
        });
        Test("Special attack and speed boost immediate, weighted contacts reset next shot",()=>
        {
            var t=Portable.Object.Instantiate(data.battle);t.deceleration=0;t.maximumShotSeconds=30;var spirit=Portable.Object.Instantiate(data.home.catalog[0]);spirit.actionSkill=ActionSkill.None;spirit.baseSpeed=600;
            var sim=new ShotSimulation(t);foreach(var c in sim.Charges)c.Active=false;sim.Prepare(false,312);Assert(sim.Launch(new(0,300),spirit),"Launch");int i=0;
            void Hit(SpecialCharge special){var c=sim.Charges[i++];c.Active=true;c.Special=special;c.Element=spirit.element;c.Position=sim.Position+sim.Velocity.normalized*5;sim.Tick(.0001f);}
            Hit(SpecialCharge.AttackBoost);Hit(SpecialCharge.None);Assert(Math.Abs(sim.WeightedContacts-(1+t.specialAttackMultiplier))<.001f,"Weighted attack");float speed=sim.Velocity.magnitude;Hit(SpecialCharge.SpeedBoost);Assert(Math.Abs(sim.Velocity.magnitude-speed*t.specialSpeedMultiplier)<.1f,"Immediate speed");
            sim.CancelShot();sim.Prepare(false,312);sim.Launch(new(0,300),spirit);Assert(sim.WeightedContacts==0&&sim.Collected==0,"Shot reset");
        });
        Test("Real master battle runs through both turns to a single terminal result",()=>
        {
            var s=Session();var quest=data.home.quests.quests.First(q=>q.category==QuestCategory.Main&&s.QuestAvailable(q.id,out _));Assert(s.TryBeginQuest(quest.id,out var run,out _),"Begin");using var b=new GameBattle(s,data.battle,run);
            int shots=0;
            for(int frame=0;frame<100000&&b.Phase!=BattlePhase.Finished;frame++)
            {
                if(b.Phase==BattlePhase.Ready){int best=Enumerable.Range(0,4).OrderByDescending(i=>b.Tuning.playerDeck[b.Deck.CardAt(i)].Attack).First();b.Select(best);b.Reposition(120+shots%4*120);Assert(b.Shoot(new PVector((shots%3-1)*95,300)),"Shot rejected");shots++;}
                b.Update(.05f);
            }
            Assert(b.Phase==BattlePhase.Finished&&b.Receipt!=null&&shots>0,"Battle never finished");Assert(s.ActiveQuestRun==null,"Quest run retained");Log($"Battle result: won={b.Receipt!.Won}, shots={shots}, turn={b.State.Turn}");
        });
        Test("Practice does not auto-finish; defeated practice retire claims once",()=>
        {
            var s=Session();var q=data.home.quests.quests.First(q=>q.IsPractice);Assert(s.TryBeginQuest(q.id,out var run,out _),"Practice begin");using var b=new GameBattle(s,data.battle,run);b.State.Enemy.ReceiveTrap(b.State.Enemy.MaxHp);b.Update(.05f);Assert(b.Phase==BattlePhase.Ready&&!b.State.Finished,"Practice auto finished");Assert(b.Retire()&&b.Receipt!.Won,"Practice retire");long coins=s.Progress.Coins;b.Retire();Assert(s.Progress.Coins==coins,"Practice double reward");
        });
        Test("Starting deck can win the first master quest using ordinary shots",()=>
        {
            bool won=false;
            for(int slot=0;slot<4&&!won;slot++)for(int position=0;position<6&&!won;position++)for(int angle=-2;angle<=2&&!won;angle++)
            {
                var s=Session();var q=data.home.quests.quests.First(q=>q.category==QuestCategory.Main&&s.QuestAvailable(q.id,out _));s.TryBeginQuest(q.id,out var run,out _);
                using var b=new GameBattle(s,data.battle,run);b.Select(slot);b.Reposition(62+position*100);b.Shoot(new PVector(angle*80,300));
                for(int frame=0;frame<600&&b.Phase!=BattlePhase.Finished&&b.Phase!=BattlePhase.Ready;frame++)b.Update(.05f);
                if(b.Receipt?.Won==true){won=true;Log($"Unmodified starting-deck victory: slot={slot}, launchX={62+position*100}, swipe=({angle*80},300), gems={s.Gems}");}
            }
            Assert(won,"No starting-deck victory found in the tested shot range");
        });
        Test("Every master quest can be entered and receives its own clear rewards",()=>
        {
            var s=Session();Fund(s);int completed=0;
            for(int round=0;round<30;round++)
            {
                bool progress=false;
                foreach(var q in data.home.quests.quests.Where(q=>!s.IsCleared(q.id)&&s.QuestAvailable(q.id,out _)))
                {
                    if(s.Energy<q.energyCost)Assert(s.TryRefillEnergy(out _),"Refill "+q.id);
                    Assert(s.TryBeginQuest(q.id,out var run,out var error),q.id+": "+error);
                    var receipt=s.CompleteQuest(run,true,data.battle);Assert(receipt.Won&&s.IsCleared(q.id),"Reward "+q.id);completed++;progress=true;
                }
                if(!progress)break;
            }
            Assert(completed==data.home.quests.quests.Length,"Unreachable quests: "+completed);Log("Quest reward routes verified: "+completed);
        });
        Test("UI: title, deck cancel, shop test purchase, summon skip and result navigation",()=>
        {
            var scene=new SceneData();var input=new InputState();using var app=new GameApplication(project,scene,input,Log,()=>now);
            void Click(string id)=>Assert(app.Activate(id),"Missing button "+id+" in "+app.Screen);
            Click("title.start");Click("nav.spirits");Click("deck.edit");var before=app.Session.DeckCopy();Click("collection."+before[0]);Assert(!app.Buttons.Any(b=>b.Id=="deck.save"&&b.Enabled),"Incomplete deck enabled");Click("back");Assert(before.SequenceEqual(app.Session.DeckCopy()),"Draft leaked");
            Click("nav.shop");Click("shop.purchase");Click("shop.tab.2");Click("shop.product.gems_80");Click("modal.confirm");Assert(app.Session.Gems==data.home.startingGems+80,"Test purchase");Assert(!app.Activate("modal.confirm"),"Double confirm");
            Click("nav.gacha");Click("gacha.banner.0");Click("gacha.draw.10");Click("modal.confirm");int gems=app.Session.Gems;var owned=Enumerable.Range(0,24).Select(app.Session.Copies).ToArray();Click("summon.skip");Click("summon.finish");Assert(app.Session.Gems==gems&&owned.SequenceEqual(Enumerable.Range(0,24).Select(app.Session.Copies)),"Skip granted again");
            Click("nav.home");Click("home.battle");Click("quest.category.0");var q=data.home.quests.quests.First(q=>q.category==QuestCategory.Main&&app.Session.QuestAvailable(q.id,out _));Click("quest."+q.id);Click("modal.confirm");Assert(app.Screen==GameScreen.Battle,"Battle route");
            var ball=app.Battle!.Simulation.Position;input.Press(new(48+ball.x,940-ball.y));input.Pointer=new(48+ball.x+30,940-ball.y-250);input.Held=false;input.Released=true;app.Update(.016f);input.EndFrame();Assert(app.Battle.Phase==BattlePhase.PlayerShot,"One-frame swipe lost");
            Click("battle.pause");float x=app.Battle.Simulation.Position.x;app.Update(.05f);Assert(x==app.Battle.Simulation.Position.x,"Pause advanced physics");Click("modal.cancel");Click("battle.pause");Click("modal.confirm");Click("modal.confirm");Assert(app.Screen==GameScreen.Home&&app.Session.ActiveQuestRun==null,"Retire route");
        });
        if(graphics)Test("DirectX 12 renders all game screens with Japanese text; GPU validation clean",()=>RenderScreens(project,review,Log));
        Log($"RESULT: {passed} passed, {failed} failed");return failed==0?0:1;
    }
    static void RenderScreens(EngineProject project,string review,Action<string> log)
    {
        using var form=new Form{ClientSize=new Size(540,960),ShowInTaskbar=false};form.Show();Application.DoEvents();
        using var renderer=new D3D12Renderer(form.Handle,540,960,true);
        using var app=new GameApplication(project,new SceneData(),new(),log,()=>new DateTimeOffset(2026,11,14,12,0,0,TimeSpan.FromHours(9)));
        void Snap(string name)
        {app.Update(.016f);renderer.Render(SceneRenderer.Build(app.Scene,project,renderer,null,false,null,log),720,1280);using var bitmap=renderer.Capture().ToBitmap();bitmap.Save(Path.Combine(review,name+".png"),ImageFormat.Png);}
        void Click(string id){Assert(app.Activate(id),"GPU missing "+id);}
        Snap("01-title");Click("title.start");Snap("02-home");Click("nav.spirits");Click("deck.edit");Snap("03-deck");Click("back");Click("spirit.upgrade");Click("collection.0");Snap("04-upgrade");
        Click("nav.shop");Snap("05-shop");Click("shop.purchase");Click("shop.tab.2");Click("shop.product.gems_80");Snap("06-confirmation");Click("modal.confirm");Click("nav.gacha");Click("gacha.banner.0");Snap("07-gacha");Click("gacha.draw.10");Click("modal.confirm");Click("summon.skip");Snap("08-summon");Click("summon.finish");Click("nav.home");Click("home.battle");Click("quest.category.0");Snap("09-quests");
        var quest=app.Data.home.quests.quests.First(q=>q.category==QuestCategory.Main&&app.Session.QuestAvailable(q.id,out _));Click("quest."+quest.id);Click("modal.confirm");Snap("10-battle");app.Battle!.Shoot(new(45,300));for(int i=0;i<100;i++)app.Update(.05f);Snap("11-shot");
        app.Battle.State.Enemy.ReceiveTrap(app.Battle.State.Enemy.MaxHp);for(int i=0;i<250&&app.Screen==GameScreen.Battle;i++)app.Update(.05f);Snap("12-result");
        Assert(renderer.DebugErrorCount==0,"D3D12 debug errors "+renderer.DebugErrorCount);log("Adapter: "+renderer.Adapter);form.Close();
    }
}
