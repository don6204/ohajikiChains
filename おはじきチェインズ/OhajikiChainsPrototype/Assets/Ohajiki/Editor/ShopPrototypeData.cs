using System;
using System.Collections.Generic;
using System.Linq;
using Ohajiki;
using UnityEditor;
using UnityEngine;

public static class ShopPrototypeData
{
    const string Root="Assets/Ohajiki/";
    public static void Configure(HubConfig home,BattleTuning tuning)
    {
        if(home.shop==null)
        {
            home.shop=AssetDatabase.LoadAssetAtPath<ShopConfig>(Root+"ShopConfig.asset");
            if(home.shop==null)
            {home.shop=CreateDefaults();AssetDatabase.CreateAsset(home.shop,Root+"ShopConfig.asset");}
            EditorUtility.SetDirty(home);
        }
        if(home.specificationVersion>=20261106)return;
        var catalog=home.catalog.ToList();
        string[] names={"焔のヒバナマル","潮のシズネ","樹のコノモリ","炎王ヒバナマル","海姫シズネ","森王コノモリ"};
        for(int i=18;i<24;i++)
        {
            string path=Root+"Data/Spirits/Spirit_"+i+".asset";
            var spirit=AssetDatabase.LoadAssetAtPath<SpiritDefinition>(path);
            if(spirit==null)
            {
                spirit=UnityEngine.Object.Instantiate(home.catalog[i%3]);
                spirit.displayName=names[i-18];spirit.rarity=i<21?4:5;
                spirit.baseHp=Mathf.RoundToInt(spirit.baseHp*(i<21?1.25f:1.5f));
                spirit.baseAttack=Mathf.RoundToInt(spirit.baseAttack*(i<21?1.25f:1.5f));
                AssetDatabase.CreateAsset(spirit,path);
            }
            if(!catalog.Contains(spirit))catalog.Add(spirit);
        }
        home.catalog=catalog.ToArray();home.maxSpiritLevel=60;
        string[] titles={"恒常ガチャ","イベントガチャ","限定ガチャ","有償ガチャ（試作）"};
        string[] descriptions={"いつでも出会えるスピリターたち。","イベントで活躍する仲間を探そう。","高レアリティのスピリターが登場。","有償枠の排出率を無料で動作確認。"};
        int[][] rates={new[]{50,30,10,7,3},new[]{0,50,27,20,3},new[]{0,50,27,22,1},new[]{0,0,55,40,5}};
        home.banners=Enumerable.Range(0,4).Select(i=>new GachaBanner{
            title=titles[i],description=descriptions[i],kind=(GachaKind)i,
            featuredIndex=Array.FindIndex(home.catalog,s=>s.rarity==5 && (int)s.element==i%3),rarityWeights=rates[i],
            pool=Enumerable.Range(0,home.catalog.Length).Select(id=>new GachaEntry{spiritIndex=id,weight=1}).ToArray()
        }).ToArray();
        tuning.squareHitboxes=true;EditorUtility.SetDirty(tuning);
        home.specificationVersion=20261106;EditorUtility.SetDirty(home);
    }
    public static ShopConfig CreateDefaults()
    {
        var config=ScriptableObject.CreateInstance<ShopConfig>();var items=new List<ShopProduct>();
        string[] elements={"火","水","木","全"};
        for(int i=0;i<4;i++)items.Add(new ShopProduct{
            id="power_"+i,title=elements[i]+"のスピリットパワー",category=ShopCategory.Items,currency=ShopCurrency.Medals,price=500,
            rewards=new[]{Reward((ShopRewardKind)((int)ShopRewardKind.FirePower+i),1)}
        });
        int[] medals={1000,2000,5000,7000,10000},gemCosts={5,10,25,30,40};
        for(int i=0;i<medals.Length;i++)items.Add(new ShopProduct{
            id="medals_"+medals[i],title=$"メダル {medals[i]:N0}枚",category=ShopCategory.Medals,currency=ShopCurrency.Gems,price=gemCosts[i],
            rewards=new[]{Reward(ShopRewardKind.Medals,medals[i])}
        });
        items.Add(new ShopProduct{id="gem_from_stones",title="宝石 1個",category=ShopCategory.GemsExchange,currency=ShopCurrency.ShiningStones,price=5,
            rewards=new[]{Reward(ShopRewardKind.Gems,1)}});
        for(int i=0;i<3;i++)items.Add(new ShopProduct{
            id="release_"+i,title=elements[i]+"のリリース記念パック",description="リリース記念 / 内容は仮設定",category=ShopCategory.Packs,
            currency=ShopCurrency.SimulatedYen,price=1000,recommended=true,portraitIndex=i,limitPeriod=ShopLimitPeriod.Session,purchaseLimit=1,
            rewards=new[]{Reward((ShopRewardKind)((int)ShopRewardKind.FirePower+i),20),Reward(ShopRewardKind.Gems,10)}
        });
        int[] gems={1,32,80,180},yen={160,2200,4900,10000};
        for(int i=0;i<gems.Length;i++)items.Add(new ShopProduct{
            id="gems_"+gems[i],title=$"宝石 {gems[i]}個",category=ShopCategory.GemsPurchase,currency=ShopCurrency.SimulatedYen,price=yen[i],
            rewards=new[]{Reward(ShopRewardKind.Gems,gems[i])}
        });
        items.Add(new ShopProduct{id="premium",title="プレミアムパス",category=ShopCategory.Premium,currency=ShopCurrency.SimulatedYen,price=800,
            limitPeriod=ShopLimitPeriod.Monthly,purchaseLimit=1});
        config.products=items.ToArray();return config;
    }
    static ShopReward Reward(ShopRewardKind kind,int amount) => new ShopReward{kind=kind,amount=amount};
}
