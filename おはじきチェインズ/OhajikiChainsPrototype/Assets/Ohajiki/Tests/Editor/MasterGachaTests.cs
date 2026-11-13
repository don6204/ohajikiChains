using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class MasterGachaTests
    {
        HubConfig config;
        BattleTuning tuning;
        DateTimeOffset now;
        PlayerSession session;
        static GachaPlayOption Play(string id,int order,int cost,int count=1,int limit=0,int reset=0)=>
            new GachaPlayOption{id=id,order=order,cost=cost,drawCount=count,purchaseLimit=limit,resetPeriod=reset};
        GachaBanner Banner(string id,int price)=>new GachaBanner{
            masterId=id,featuredIndex=12,rarityWeights=null,
            pool=new[]{new GachaEntry{masterId=id+"_sp",spiritIndex=12,weight=1}},
            plays=new[]{Play("first",1,1,limit:1),Play("single",2,price),Play("ten",3,price*10,10)}
        };
        [SetUp] public void Setup()
        {
            now=new DateTimeOffset(2026,11,13,3,0,0,TimeSpan.Zero);
            config=ScriptableObject.CreateInstance<HubConfig>();tuning=ScriptableObject.CreateInstance<BattleTuning>();
            config.startingGems=500;
            config.catalog=Enumerable.Range(0,13).Select(i=>{
                var spirit=ScriptableObject.CreateInstance<SpiritDefinition>();
                spirit.displayName="master test "+i;spirit.element=(Element)(i%3);return spirit;
            }).ToArray();
            config.banners=new[]{Banner("A",5),Banner("B",9)};
            session=new PlayerSession(config,tuning,()=>now);
        }
        [TearDown] public void Cleanup()
        {
            foreach(var spirit in config.catalog)UnityEngine.Object.DestroyImmediate(spirit);
            UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(tuning);
        }
        [Test] public void BannerSpecificPricesAndFirstLimitsAreIndependent()
        {
            Assert.IsTrue(session.TryDraw(0,1,out var first,out string error),error);Assert.AreEqual(1,first.Cost);
            Assert.AreEqual(5,session.GachaCost(0,1));Assert.AreEqual(1,session.GachaCost(1,1));
            Assert.IsTrue(session.TryDraw(1,1,out _,out error),error);Assert.AreEqual(9,session.GachaCost(1,1));
            Assert.IsTrue(session.TryDraw(1,10,out var ten,out error),error);
            Assert.AreEqual(90,ten.Cost);Assert.AreEqual(409,session.Gems);
            Assert.IsTrue(session.FirstSingleUsed(0));Assert.IsTrue(session.FirstSingleUsed(1));
        }
        [Test] public void ExactLocalStartAndEndTimesGateSpending()
        {
            var banner=config.banners[0];banner.availableFrom="2026-11-13T12:00:00";banner.availableUntil="2026-11-13T12:00:02";
            now=new DateTimeOffset(2026,11,13,2,59,59,TimeSpan.Zero);
            Assert.IsFalse(session.TryDraw(0,1,out _,out _));Assert.AreEqual(500,session.Gems);
            now=now.AddSeconds(1);Assert.IsTrue(session.TryDraw(0,1,out _,out _));
            now=now.AddSeconds(2);Assert.IsFalse(session.TryDraw(0,1,out _,out _));Assert.AreEqual(499,session.Gems);
            banner.availableFrom="2026-11-13T03:00:00+00:00";banner.availableUntil="2026-11-13T03:00:02+00:00";
            Assert.IsFalse(banner.IsOpen(now));Assert.IsTrue(banner.IsOpen(now.AddSeconds(-1)));
        }
        [TestCase(1,2026,11,13,2026,11,14)]
        [TestCase(2,2026,11,15,2026,11,16)]
        [TestCase(3,2026,11,30,2026,12,1)]
        [TestCase(4,2026,12,31,2027,1,1)]
        public void CalendarLimitsResetAtTheLocalBoundary(int period,int y,int m,int d,int nextY,int nextM,int nextD)
        {
            config.banners[0].plays=new[]{Play("limited",1,7,limit:1,reset:period)};
            now=new DateTimeOffset(y,m,d,23,59,59,TimeSpan.FromHours(9));
            Assert.IsTrue(session.CanDraw(0,1,out _));Assert.IsTrue(session.CanDraw(0,1,out _));
            Assert.IsTrue(session.TryDraw(0,1,out _,out _));Assert.IsFalse(session.TryDraw(0,1,out _,out _));
            now=new DateTimeOffset(nextY,nextM,nextD,0,0,0,TimeSpan.FromHours(9));
            Assert.IsTrue(session.TryDraw(0,1,out _,out _));Assert.AreEqual(486,session.Gems);
        }
        [Test] public void RejectedPurchaseDoesNotConsumeLimitAndNoneDoesNotReset()
        {
            config.banners[0].plays=new[]{Play("limited",1,501,limit:1)};
            Assert.IsFalse(session.TryDraw(0,1,out _,out _));Assert.AreEqual(0,session.Copies(12));
            config.banners[0].plays[0].cost=7;
            Assert.IsTrue(session.TryDraw(0,1,out _,out _));now=now.AddYears(1);
            Assert.IsFalse(session.TryDraw(0,1,out _,out _));Assert.AreEqual(493,session.Gems);
        }
        [Test] public void WeightedEntriesGrantTheirQuantitiesOnceAndKeepTheReceiptImmutable()
        {
            config.banners[0].pool[0].quantity=3;
            Assert.IsTrue(session.TryDraw(0,10,out var receipt,out string error),error);
            Assert.AreEqual(30,session.Copies(12));Assert.AreEqual(10,receipt.SpiritIds.Count);
            Assert.IsTrue(receipt.Quantities.All(n=>n==3));Assert.IsTrue(receipt.NewSpirits[0]);
            Assert.IsTrue(receipt.NewSpirits.Skip(1).All(n=>!n));
            Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<int>)receipt.Quantities)[0]=1);
            config.banners[0].pool=new[]{new GachaEntry{spiritIndex=0,weight=1},new GachaEntry{spiritIndex=12,weight=3}};
            Assert.AreEqual(.25,GachaRules.Probability(config,config.banners[0],0),1e-9);
            Assert.AreEqual(.75,GachaRules.Probability(config,config.banners[0],12),1e-9);
        }
        [Test] public void DrawUsesOneClockSnapshotForPriceAndLimitBucket()
        {
            config.banners[0].plays[0].resetPeriod=1;
            int reads=0;
            session=new PlayerSession(config,tuning,()=>{reads++;return now;});
            reads=0;Assert.IsTrue(session.TryDraw(0,1,out _,out _));Assert.AreEqual(1,reads);
        }
        [Test] public void MasterConfigurationRejectsUnsupportedOrAmbiguousOptions()
        {
            config.banners[0].plays[0].paidOnly=true;Assert.IsFalse(config.Validate(out string error));StringAssert.Contains("有償",error);
            config.banners[0].plays[0].paidOnly=false;config.banners[0].plays[1].id="first";Assert.IsFalse(config.Validate(out _));
            config.banners[0].plays[1].id="single";config.banners[0].plays[1].drawCount=2;Assert.IsFalse(config.Validate(out _));
            config.banners[0].plays[1].drawCount=1;config.banners[0].pool[0].quantity=0;Assert.IsFalse(config.Validate(out _));
        }
    }
}
