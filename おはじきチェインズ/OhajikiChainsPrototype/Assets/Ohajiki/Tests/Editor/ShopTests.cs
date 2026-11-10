using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class ShopTests
    {
        HubConfig home;
        ShopConfig config;
        BattleTuning battle;
        PlayerSession session;
        DateTimeOffset now;
        ShopState Shop=>session.Shop;
        [SetUp] public void Setup()
        {
            now=new DateTimeOffset(2026,11,6,3,0,0,TimeSpan.Zero);
            home=ScriptableObject.CreateInstance<HubConfig>();config=ScriptableObject.CreateInstance<ShopConfig>();
            battle=ScriptableObject.CreateInstance<BattleTuning>();home.shop=config;
            home.catalog=Enumerable.Range(0,15).Select(i=>{
                var spirit=ScriptableObject.CreateInstance<SpiritDefinition>();spirit.rarity=i%5+1;return spirit;
            }).ToArray();
            home.banners=new[]{new GachaBanner{featuredIndex=14,rarityWeights=new[]{50,30,10,7,3},
                pool=Enumerable.Range(0,15).Select(i=>new GachaEntry{spiritIndex=i,weight=1}).ToArray()}};
            config.products=new[]{
                Product("power",ShopCategory.Items,ShopCurrency.Medals,500,ShopRewardKind.FirePower,1),
                Product("medals",ShopCategory.Medals,ShopCurrency.Gems,5,ShopRewardKind.Medals,1000),
                Product("stones",ShopCategory.GemsExchange,ShopCurrency.ShiningStones,5,ShopRewardKind.Gems,1),
                Product("gems",ShopCategory.GemsPurchase,ShopCurrency.SimulatedYen,10000,ShopRewardKind.Gems,180),
                new ShopProduct{id="pack",title="Pack",category=ShopCategory.Packs,currency=ShopCurrency.SimulatedYen,price=1000,
                    limitPeriod=ShopLimitPeriod.Session,purchaseLimit=1,rewards=new[]{R(ShopRewardKind.WaterPower,20),R(ShopRewardKind.Gems,10)}},
                new ShopProduct{id="premium",title="Premium",category=ShopCategory.Premium,currency=ShopCurrency.SimulatedYen,price=800,
                    limitPeriod=ShopLimitPeriod.Monthly,purchaseLimit=1},
                Product("event",ShopCategory.Event,ShopCurrency.EventPoints,10,ShopRewardKind.AllPower,1)
            };
            session=new PlayerSession(home,battle,()=>now);
        }
        static ShopReward R(ShopRewardKind kind,int amount)=>new ShopReward{kind=kind,amount=amount};
        static ShopProduct Product(string id,ShopCategory category,ShopCurrency currency,int price,ShopRewardKind reward,int amount)
            =>new ShopProduct{id=id,title=id,category=category,currency=currency,price=price,rewards=new[]{R(reward,amount)}};
        void Buy(string id) {var quote=Shop.Prepare(id,out string reason);Assert.NotNull(quote,reason);Assert.IsTrue(Shop.Confirm(quote,out reason),reason);}
        [TearDown] public void Teardown()
        {
            foreach(var spirit in home.catalog)UnityEngine.Object.DestroyImmediate(spirit);
            UnityEngine.Object.DestroyImmediate(home);UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(battle);
        }
        [Test] public void CancelNeverSpendsAndTheSameConfirmationCannotApplyTwice()
        {
            var quote=Shop.Prepare("power",out _);Shop.Cancel(quote);
            Assert.IsFalse(Shop.Confirm(quote,out _));Assert.AreEqual(5000,session.Progress.Coins);Assert.AreEqual(20,session.MaterialCount(0));
            quote=Shop.Prepare("power",out _);Assert.IsTrue(Shop.Confirm(quote,out _));Assert.IsFalse(Shop.Confirm(quote,out _));
            Assert.AreEqual(4500,session.Progress.Coins);Assert.AreEqual(21,session.MaterialCount(0));
        }
        [Test] public void CashSimulationGrantsSpecifiedGemsWithoutDebitingGameCurrency()
        {
            Buy("gems");Assert.AreEqual(180,session.Gems);Assert.AreEqual(5000,session.Progress.Coins);
            Buy("medals");Assert.AreEqual(175,session.Gems);Assert.AreEqual(6000,session.Progress.Coins);
            Buy("stones");Assert.AreEqual(176,session.Gems);Assert.AreEqual(15,session.ShiningStones);
        }
        [Test] public void PackGivesBothRewardsAndIsLimitedAcrossSharedListings()
        {
            Buy("pack");Assert.AreEqual(40,session.MaterialCount(1));Assert.AreEqual(10,session.Gems);
            Assert.IsNull(Shop.Prepare("pack",out _));Assert.AreEqual(0,Shop.Remaining(Shop.Find("pack")));
        }
        [Test] public void InsufficientBalanceAndChangedOfferDoNotPartiallyApply()
        {
            Assert.IsNull(Shop.Prepare("medals",out _));
            var quote=Shop.Prepare("power",out _);Shop.Find("power").price=600;
            Assert.IsFalse(Shop.Confirm(quote,out _));Assert.AreEqual(5000,session.Progress.Coins);Assert.AreEqual(20,session.MaterialCount(0));
            quote=Shop.Prepare("power",out _);session.Progress.TrySpendCoins(5000);
            Assert.IsFalse(Shop.Confirm(quote,out _));Assert.AreEqual(20,session.MaterialCount(0));
        }
        [Test] public void InvalidRewardsAndInventoryOverflowRejectTheWholeTransaction()
        {
            Assert.IsFalse(session.ApplyShop(ShopCurrency.Medals,500,new[]{R(ShopRewardKind.Gems,1),R(ShopRewardKind.FirePower,-1)},out _));
            Assert.AreEqual(0,session.Gems);Assert.AreEqual(5000,session.Progress.Coins);
            Assert.IsTrue(session.ApplyShop(ShopCurrency.SimulatedYen,0,new[]{R(ShopRewardKind.Gems,int.MaxValue)},out _));
            Assert.IsNull(Shop.Prepare("gems",out _));Assert.AreEqual(int.MaxValue,session.Gems);
        }
        [Test] public void EventLockIsEnforcedAtConfirmationAsWellAsInTheUi()
        {
            Assert.IsNull(Shop.Prepare("event",out _));config.eventActive=true;
            session.ApplyShop(ShopCurrency.SimulatedYen,0,new[]{R(ShopRewardKind.EventPoints,10)},out _);
            var quote=Shop.Prepare("event",out _);config.eventActive=false;
            Assert.IsFalse(Shop.Confirm(quote,out _));Assert.AreEqual(10,session.EventPoints);
            config.eventActive=true;Buy("event");Assert.AreEqual(0,session.EventPoints);Assert.AreEqual(21,session.MaterialCount(3));
        }
        [Test] public void SaleWindowUsesJapanDateAndExcludesEndDate()
        {
            var p=Shop.Find("power");p.availableFrom="2026-11-07";p.availableUntil="2026-11-08";
            now=new DateTimeOffset(2026,11,6,14,59,0,TimeSpan.Zero);Assert.IsNull(Shop.Prepare("power",out _));
            now=now.AddMinutes(1);Assert.NotNull(Shop.Prepare("power",out _));
            now=now.AddDays(1);Assert.IsNull(Shop.Prepare("power",out _));
        }
        [Test] public void DailyLimitResetsAtMidnightAndOldConfirmationIsRejected()
        {
            var p=Shop.Find("power");p.limitPeriod=ShopLimitPeriod.Daily;p.purchaseLimit=1;
            Buy("power");Assert.IsNull(Shop.Prepare("power",out _));
            now=now.AddDays(1);var quote=Shop.Prepare("power",out _);Assert.NotNull(quote);
            now=now.AddDays(1);Assert.IsFalse(Shop.Confirm(quote,out _));Buy("power");Assert.AreEqual(4000,session.Progress.Coins);
        }
        [Test] public void MonthlyPassExpiresWithoutRenewalAndDailyBonusIsOncePerDay()
        {
            now=new DateTimeOffset(2026,11,30,14,59,0,TimeSpan.Zero);
            Buy("premium");Assert.IsTrue(Shop.PremiumActive);Assert.AreEqual(2,session.Gems);
            Assert.IsFalse(Shop.ClaimDailyBonus());Assert.IsNull(Shop.Prepare("premium",out _));
            now=now.AddMinutes(1);Shop.RefreshCalendar();Assert.IsFalse(Shop.PremiumActive);Assert.AreEqual(2,session.Gems);
            Buy("premium");Assert.AreEqual(4,session.Gems);
            now=now.AddDays(1);Assert.IsTrue(Shop.RefreshCalendar());Assert.AreEqual(6,session.Gems);
            Assert.IsFalse(Shop.RefreshCalendar());Assert.AreEqual(6,session.Gems);
            now=now.AddDays(5);Shop.RefreshCalendar();Assert.AreEqual(8,session.Gems); // No missed-day accumulation.
        }
        [TestCase(50,30,10,7,3)] [TestCase(0,50,27,20,3)] [TestCase(0,50,27,22,1)] [TestCase(0,0,55,40,5)]
        public void GachaProbabilitiesMatchRarityTotalsEvenWithUnequalCharacterWeights(int one,int two,int three,int four,int five)
        {
            var banner=home.banners[0];banner.rarityWeights=new[]{one,two,three,four,five};banner.pool[0].weight=9;
            Assert.IsTrue(home.Validate(out string error),error);
            for(int star=1;star<=5;star++)
                Assert.AreEqual(banner.rarityWeights[star-1]/100.0,Enumerable.Range(0,15).Where(i=>home.catalog[i].rarity==star).Sum(i=>GachaRules.Probability(home,banner,i)),1e-9);
            for(int i=0;i<1000;i++)Assert.Greater(banner.rarityWeights[home.catalog[GachaRules.Draw(home,banner,new System.Random(i))].rarity-1],0);
        }
        [Test] public void GachaRejectsUnpopulatedRarityAndExpiredBanner()
        {
            home.banners[0].pool=home.banners[0].pool.Where(e=>home.catalog[e.spiritIndex].rarity!=5).ToArray();
            Assert.IsFalse(home.Validate(out _));
            home.banners[0].availableUntil="2026-11-06";Assert.IsFalse(session.BannerOpen(0));
            Assert.IsFalse(session.TryDraw(0,1,out _,out _));
        }
        [Test] public void LevelSixtyIsAllowedButSixtyOneIsRejected()
        {
            home.catalog[0].level=59;session=new PlayerSession(home,battle,()=>now);
            Assert.IsTrue(session.Upgrade(0,0,1));Assert.AreEqual(60,session.Level(0));Assert.IsFalse(session.Upgrade(0,0,1));
        }
        [Test] public void SquareCornerContactReflectsOnABoxFaceAndCollectsOnlyOnce()
        {
            var spirit=home.catalog[0];spirit.baseSpeed=100;
            var sim=new ShotSimulation(battle);foreach(var charge in sim.Charges)charge.Active=false;
            sim.Charges[0].Active=true;sim.Charges[0].Position=sim.Position+new Vector2(40,47.5f);
            sim.Launch(Vector2.up*100,spirit);sim.Tick(.01f);
            Assert.AreEqual(1,sim.Collected);Assert.LessOrEqual(sim.Velocity.y,0);
            sim.Tick(.01f);Assert.AreEqual(1,sim.Collected);
            battle.squareHitboxes=false;sim=new ShotSimulation(battle);foreach(var charge in sim.Charges)charge.Active=false;
            sim.Charges[0].Active=true;sim.Charges[0].Position=sim.Position+new Vector2(40,47.5f);
            sim.Launch(Vector2.up*100,spirit);sim.Tick(.01f);Assert.AreEqual(0,sim.Collected);
        }
    }
}

