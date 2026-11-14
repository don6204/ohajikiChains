namespace Ohajiki.Engine;

public sealed partial class GameApplication
{
    void GachaList()
    {
        StartList(175,1150,Data.home.banners.Length*330);
        for(int i=0;i<Data.home.banners.Length;i++)
        {
            int index=i;var b=Data.home.banners[i];float y=175+i*330-scroll;if(y+305<175||y>1150)continue;
            Rect("ガチャバナー",24,y,672,305,Ink);Portrait(Data.home.catalog[b.featuredIndex],430,y+5,190,285);
            Label(b.title,40,y+20,385,65,33,Paper,true);
            Label(b.description??"スピリターたちとの、新しい出会い。",45,y+88,365,80,23,Paper);
            Button("gacha.banner."+i,Session.BannerOpen(i)?"ガチャ詳細":"開催期間外",65,y+205,330,68,()=>{bannerIndex=index;Go(GameScreen.Banner);},Session.BannerOpen(i),Teal,27,listClip);
        }
        EndList();Rect("ガチャ下部",0,1150,720,66,Paper);Header("ガチャ");
    }
    void Banner()
    {
        var b=Data.home.banners[bannerIndex];var featured=b.featuredIndices is {Length:>0}?b.featuredIndices:[b.featuredIndex];
        var s=Data.home.catalog[featured[(int)(screenAge/Math.Max(.1f,Data.home.featuredCycleSeconds))%featured.Length]];
        Portrait(s,185,175,350,525);Label(s.displayName,40,705,640,60,38,Ink,true);Label(new string('★',s.rarity),120,763,480,45,32,Red);
        Button("gacha.rates","提供割合を確認",135,832,450,60,()=>
        {
            var lines=Enumerable.Range(1,5).Reverse().Select(r=>$"★{r}：{Enumerable.Range(0,Data.home.catalog.Length).Where(i=>Data.home.catalog[i].rarity==r).Sum(i=>GachaRules.Probability(Data.home,b,i))*100:0.##}%");
            Information("提供割合",string.Join("\n",lines)+$"\n\n目玉：{s.displayName}\n排出率 {GachaRules.Probability(Data.home,b,Array.IndexOf(Data.home.catalog,s))*100:0.###}%\n\n期間\n{b.availableFrom}\n〜 {b.availableUntil}\n※MasterDataの重みに基づく割合です。");
        },color:Ink,font:25);
        foreach(int count in new[]{1,10})
        {
            int cost=Session.GachaCost(bannerIndex,count);bool can=Session.CanDraw(bannerIndex,count,out var reason);
            float x=count==1?35:375;
            Button("gacha.draw."+count,$"{count}連　宝石 {cost}個",x,930,310,86,()=>Confirm("ガチャを引く",$"{b.title}\n{count}連を引きますか？\n\n必要宝石 {cost}個\n所持宝石 {Session.Gems}個"+(count==10?$"\nおまけ：宝石 {Data.home.tenBonusGems}個":""),()=>DrawGacha(count),"引く"),can,Teal,28);
            Label(can?(count==1&&!Session.FirstSingleUsed(bannerIndex)?"初回割引":""):reason,x,1026,310,82,20,Muted);
        }
        Back(GameScreen.Gacha);Header(b.title);
    }
    void DrawGacha(int count)
    {
        if(!Session.TryDraw(bannerIndex,count,out var receipt,out var error)){Information("抽選できません",error);return;}
        GachaReceipt=receipt;SummonIndex=SummonPhase=0;summonAge=0;Go(GameScreen.Summon);log($"ガチャ確定：{receipt.SpiritIds.Count}体 / 宝石 -{receipt.Cost} +{receipt.Bonus}");
    }
    void SkipSummon(){SummonPhase=GachaReceipt!.SpiritIds.Count==10?4:3;summonAge=0;}
    void NextSummon()
    {
        if(GachaReceipt==null||SummonPhase!=3||summonAge<MasterConstants.GetFloat("GachaSequence.revealNextDelay",.45f))return;
        if(GachaReceipt.SpiritIds.Count==1){Go(GameScreen.Gacha);return;}
        SummonIndex++;SummonPhase=SummonIndex>=GachaReceipt.SpiritIds.Count?4:1;summonAge=0;
    }
    void Summon()
    {
        var receipt=GachaReceipt!;int index=Math.Min(SummonIndex,receipt.SpiritIds.Count-1);var s=Data.home.catalog[receipt.SpiritIds[index]];
        Image("Assets/Imported/Resources/UI/SpiritSanctuary.png",0,0,720,1280);
        if(SummonPhase<3)
        {
            Label(new[]{"精霊の遺跡","想いが、宿る。","解き放て！"}[SummonPhase],40,180,640,100,45,Paper,true);
            float drop=Math.Clamp(summonAge/MasterConstants.GetFloat("GachaSequence.dropSeconds",1.25f),0,1);
            float y=SummonPhase==0?360+MathF.Sin(age*2)*12:SummonPhase==1?180+drop*drop*450:630;
            float size=SummonPhase==2?120+summonAge*550:130;
            Rect("召喚光",360-size/2,y-size/2,size,size,Gold,"円",SummonPhase==2?.8f:.35f);
            Rect("精霊球",300,y-60,120,120,ElementColors[(int)s.element],"円");
            Portrait(s,327,y-50,66,100,.8f);
            if(SummonPhase==2)for(int i=0;i<16;i++)
            {float a=i*MathF.PI/8+age*.3f,d=100+summonAge*600;Rect("光片",360+MathF.Cos(a)*d,y+MathF.Sin(a)*d,12,12,Gold,"円",Math.Max(0,1-summonAge));}
            Label($"{index+1} / {receipt.SpiritIds.Count}",220,1000,280,70,32,Paper,true);
        }
        else if(SummonPhase==3)
        {
            Rect("獲得背景",0,0,720,1280,Paper,opacity:.92f);Label(receipt.NewSpirits[index]?"NEW！":"スピリター獲得",40,100,640,95,45,Red,true);
            Portrait(s,160,250,400,600);Label(new string('★',s.rarity),100,202,520,55,42,Red,true);
            Label(s.displayName,35,875,650,80,38,Ink,true);Label($"を手に入れた  ×{receipt.Quantities[index]}",50,960,620,60,28);
            Button("summon.next","つぎへ",130,1090,460,88,NextSummon,summonAge>=MasterConstants.GetFloat("GachaSequence.revealNextDelay",.45f),Teal,32);
        }
        else
        {
            Rect("結果用紙",0,0,720,1280,Paper,opacity:.95f);Label("ガチャ結果",40,100,640,100,48,Ink,true);
            for(int i=0;i<receipt.SpiritIds.Count;i++)
            {
                int n=i,id=receipt.SpiritIds[i];float x=20+i%5*138,y=300+i/5*310;
                SmallSpirit(id,x,y,128,252,()=>ShowSpirit(Data.home.catalog[id],id),"summon.result."+n);
                if(receipt.NewSpirits[i])Label("NEW",x,y-35,128,35,23,Red,true);
            }
            Label($"宝石 −{receipt.Cost}　おまけ ＋{receipt.Bonus}\n現在の宝石 {Session.Gems}個",45,935,630,100,28);
            Button("summon.finish","つぎへ",130,1110,460,88,()=>Go(GameScreen.Gacha),font:32);
        }
        if(SummonPhase<4)Button("summon.skip","スキップ",535,30,160,65,SkipSummon,color:Ink,font:24);
    }
    static string CurrencyName(ShopCurrency c)=>c switch{ShopCurrency.Medals=>"メダル",ShopCurrency.Gems=>"宝石",ShopCurrency.ShiningStones=>"輝く石",ShopCurrency.EventPoints=>"イベントP",_=>"円（テスト）"};
    void Shop()
    {
        var shop=Session.Shop;
        ShopCategory[] categories=shopPurchase?[ShopCategory.Packs,ShopCategory.Packs,ShopCategory.GemsPurchase,ShopCategory.Premium]:[ShopCategory.Items,ShopCategory.Medals,ShopCategory.GemsExchange,ShopCategory.Event];
        string[] tabs=shopPurchase?["おすすめ","パック","宝石購入","プレミアム"]:["アイテム","メダル","宝石生成","イベント"];
        var selected=categories[shopTab];
        var products=shop.Config.products.Where(p=>shopPurchase&&shopTab==0?p.recommended:p.category==selected).OrderBy(p=>p.discounted).ToArray();
        StartList(380,1140,products.Length*237);
        for(int i=0;i<products.Length;i++)
        {
            var p=products[i];float y=380+i*237-scroll;if(y+220<380||y>1140)continue;
            bool allowed=shop.CanPurchase(p,out string reason);Rect("商品",24,y,672,220,Paper);
            Label(p.title,40,y+12,640,50,30,Ink,true);
            string rewards=string.Join(" / ",p.rewards.Select(r=>PlayerSession.QuestRewardName(r.kind)+" ×"+r.amount));
            if(p.category==ShopCategory.Premium)rewards=$"毎日宝石 {shop.Config.premiumDailyGems}個 / 毎月1日更新・自動更新なし";
            Label(rewards+(string.IsNullOrEmpty(p.description)?"":"\n"+p.description),38,y+67,635,73,21,Muted);
            int remain=shop.Remaining(p);
            Label(allowed?$"{p.price:N0} {CurrencyName(p.currency)}"+(remain<int.MaxValue?$" / 残り {remain}":""):reason,38,y+150,430,55,24,Ink,true);
            Button("shop.product."+p.id,shopPurchase?"テスト購入":"交換",490,y+145,185,60,()=>ShopConfirm(p),allowed,shopPurchase?Red:Teal,24,listClip);
        }
        EndList();Rect("ショップ上部",0,150,720,230,Paper);Rect("ショップ下部",0,1140,720,76,Paper);
        Button("shop.exchange","交換",24,172,328,62,()=>{shopPurchase=false;shopTab=0;scroll=0;},color:shopPurchase?Ink:Teal);
        Button("shop.purchase","購入",368,172,328,62,()=>{shopPurchase=true;shopTab=0;scroll=0;},color:shopPurchase?Red:Ink);
        for(int i=0;i<4;i++){int tab=i;Button("shop.tab."+i,tabs[i],20+i*175,257,166,58,()=>{shopTab=tab;scroll=0;},color:shopTab==i?Teal:Ink,font:22);}
        Label(shopPurchase?"テスト購入です。実際の請求はありません。":$"輝く石 {Session.ShiningStones}  /  イベントP {Session.EventPoints}",26,327,668,48,22,Muted);
        if(products.Length==0)Label(selected==ShopCategory.Event?"イベント開催前です":"現在の商品はありません",40,600,640,100,30);
        if(shopPurchase)Button("shop.legal","販売に関する表記",170,1150,380,55,()=>Information("販売に関する表記",shop.Config.legalNotice),color:Paper,font:23);
        Header("ショップ");
    }
    void ShopConfirm(ShopProduct product)
    {
        var quote=Session.Shop.Prepare(product.id,out var reason);
        if(quote==null){Information("購入できません",reason);return;}
        Confirm(product.currency==ShopCurrency.SimulatedYen?"テスト購入の確認":"交換の確認",$"{product.title}\n\n{product.price:N0} {CurrencyName(product.currency)}\n\n"+string.Join("\n",product.rewards.Select(r=>PlayerSession.QuestRewardName(r.kind)+" ×"+r.amount))+(product.currency==ShopCurrency.SimulatedYen?"\n\n実際の請求は発生しません。":""),()=>
        {if(Session.Shop.Confirm(quote,out var message))Notice(message);else Information("反映できません",message);},product.currency==ShopCurrency.SimulatedYen?"テスト購入":"交換する",()=>Session.Shop.Cancel(quote));
    }
}
