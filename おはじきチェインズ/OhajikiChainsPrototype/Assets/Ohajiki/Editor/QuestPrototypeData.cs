using System.Collections.Generic;
using System.Linq;
using Ohajiki;
using UnityEditor;
using UnityEngine;

public static class QuestPrototypeData
{
    public static void Configure(HubConfig home)
    {
        if(home.quests!=null)return;
        const string path="Assets/Ohajiki/QuestCatalog.asset";
        var catalog=AssetDatabase.LoadAssetAtPath<QuestCatalog>(path);
        if(catalog==null)
        {
            catalog=ScriptableObject.CreateInstance<QuestCatalog>();
            catalog.quests=CreateDefaults(home.catalog.Length);
            AssetDatabase.CreateAsset(catalog,path);
        }
        home.quests=catalog;EditorUtility.SetDirty(home);
        if(home.shop!=null)
        {
            home.shop.eventActive=true;
            if(!home.shop.products.Any(p=>p.category==ShopCategory.Event))
                home.shop.products=home.shop.products.Concat(new[]{
                    new ShopProduct{id="event_power",title="灯火祭・全のスピリットパワー",category=ShopCategory.Event,
                        currency=ShopCurrency.EventPoints,price=30,rewards=new[]{new ShopReward{kind=ShopRewardKind.AllPower,amount=1}}},
                    new ShopProduct{id="event_stone",title="灯火祭・輝く石",category=ShopCategory.Event,
                        currency=ShopCurrency.EventPoints,price=50,limitPeriod=ShopLimitPeriod.Daily,purchaseLimit=3,
                        rewards=new[]{new ShopReward{kind=ShopRewardKind.ShiningStones,amount=1}}}
                }).ToArray();
            EditorUtility.SetDirty(home.shop);
        }
    }
    // Provisional content is created once. Subsequent builds retain Inspector / Excel edits.
    public static QuestDefinition[] CreateDefaults(int spiritCount)
    {
        var quests=new List<QuestDefinition>();
        string[] mainNames={"1-1 はじまりの遺跡","1-2 水音の回廊","1-3 芽吹きの祭壇"};
        string[] eventNames={"灯火祭・火花の入口","灯火祭・水鏡の庭","灯火祭・大樹の広場"};
        string[] showdownNames={"炎の守護者 決戦","水の守護者 決戦","森の守護者 決戦"};
        for(int i=0;i<3;i++)
        {
            quests.Add(new QuestDefinition{id="main_0"+(i+1),title=mainNames[i],description="遺跡をめぐり、仲間と成長する冒険。",
                category=QuestCategory.Main,order=i+1,enemyElement=(Element)i,hpMultiplier=.65f+i*.2f,attackMultiplier=.6f+i*.15f,
                experience=120+i*50,medals=500+i*150,firstClearGems=1,prerequisiteId=i==0?"":"main_0"+i});
            quests.Add(new QuestDefinition{id="event_0"+(i+1),title=eventNames[i],description="灯火祭の仮イベント。開催期間は後から設定できます。",
                category=QuestCategory.Event,order=i+1,enemyElement=(Element)i,hpMultiplier=.8f+i*.3f,attackMultiplier=.7f+i*.2f,
                experience=50+i*10,medals=400+i*200,eventPoints=30+i*20,energyCost=1,firstClearGems=1});
            for(int d=0;d<3;d++)
                quests.Add(new QuestDefinition{id=$"showdown_0{i+1}_{new[]{"easy","normal","hard"}[d]}",groupId="showdown_0"+(i+1),title=showdownNames[i],
                    description="守護者に挑戦。各難易度の初回はスピリターを確定獲得。",category=QuestCategory.Showdown,order=i+1,
                    difficulty=(QuestDifficulty)(d+1),enemyElement=(Element)i,hpMultiplier=new[]{.8f,1.5f,2.7f}[d],
                    attackMultiplier=new[]{.65f,1.05f,1.65f}[d],speedMultiplier=1+d*.08f,energyCost=d+1,
                    experience=50+d*20,medals=500+d*400,firstClearGems=1,
                    rewardSpiritIndex=spiritCount>14?12+i:i,firstClearSpirit=true,spiritDropChance=new[]{.15f,.4f,1f}[d]});
        }
        for(int i=0;i<4;i++)
            quests.Add(new QuestDefinition{id="material_0"+(i+1),title=new[]{"火","水","木","全"}[i]+"の力を集めよう",description="スピリットパワーを集める育成クエスト。",
                category=QuestCategory.Materials,order=i+1,enemyElement=(Element)(i%3),hpMultiplier=.9f,attackMultiplier=.7f,
                experience=30,medals=250,materialType=i,materialCount=i==3?3:5,energyCost=1});
        for(int i=0;i<5;i++)
            quests.Add(new QuestDefinition{id="challenge_0"+(i+1),title=$"挑戦の塔 第{i+1}層",description="前の階層をクリアして次の階層へ。奥ほど強敵と豪華な初回報酬。",
                category=QuestCategory.Challenge,order=i+1,enemyElement=(Element)(i%3),prerequisiteId=i==0?"":"challenge_0"+i,
                hpMultiplier=new[]{1f,1.5f,2.2f,3f,4f}[i],attackMultiplier=.8f+i*.3f,speedMultiplier=1+i*.06f,
                experience=60+i*10,medals=600+i*400,firstClearGems=i+1,shiningStones=i+1,energyCost=i<2?1:i<4?2:3});
        quests.Add(new QuestDefinition{id="practice_01",title="反射とスキルの練習",description="消費エネルギーなし。敵HPが0になってもショットを続けられます。",
            category=QuestCategory.Practice,energyCost=0,experience=0,medals=0,hpMultiplier=.5f,attackMultiplier=.25f,enemyElement=Element.Wood});
        return quests.ToArray();
    }
}
