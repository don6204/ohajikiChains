using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class HubTests
    {
        HubConfig config;
        BattleTuning tuning;
        PlayerSession session;
        [SetUp] public void Setup()
        {
            tuning=ScriptableObject.CreateInstance<BattleTuning>();
            config=ScriptableObject.CreateInstance<HubConfig>();config.startingGems=200;
            config.catalog=Enumerable.Range(0,18).Select(i=>{
                var spirit=ScriptableObject.CreateInstance<SpiritDefinition>();
                spirit.displayName="Spirit "+i.ToString("00");spirit.element=(Element)(i%3);
                spirit.rarity=i<12?1:3;return spirit;
            }).ToArray();
            tuning.playerDeck=config.catalog.Take(12).ToArray();
            tuning.enemyDeck=config.catalog.Take(12).ToArray();
            config.banners=new[]{new GachaBanner{featuredIndex=12,pool=new[]{
                new GachaEntry{spiritIndex=0,weight=0},new GachaEntry{spiritIndex=12,weight=5}
            }}};
            session=new PlayerSession(config,tuning);
        }
        int Draw() { Assert.IsTrue(session.TryDraw(0,1,out var receipt,out string error),error);return receipt.SpiritIds[0]; }
        [TearDown] public void TearDown()
        {
            foreach(var spirit in config.catalog)UnityEngine.Object.DestroyImmediate(spirit);
            UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(tuning);
        }
        [Test] public void DraftIsIsolatedUntilValidOwnedTwelveAreConfirmed()
        {
            int[] original=session.DeckCopy(),draft=session.DeckCopy();
            draft[0]=12;
            Assert.IsFalse(session.SaveDeck(draft));
            CollectionAssert.AreEqual(original,session.DeckCopy());
            Draw();Assert.IsTrue(session.SaveDeck(draft));
            draft[1]=17;Assert.AreEqual(1,session.DeckCopy()[1]);
            Assert.IsFalse(session.SaveDeck(original.Take(11).ToArray()));
            Assert.IsFalse(session.SaveDeck(Enumerable.Repeat(0,12).ToArray()));
            Assert.IsFalse(session.SaveDeck(original.Select(i=>i==0?-1:i).ToArray()));
        }
        [Test] public void DeckSlotsRemainIndependentAndWrapInBothDirections()
        {
            int[] other=session.DeckCopy();Draw();
            int[] replacement=session.DeckCopy();replacement[0]=12;session.SaveDeck(replacement);
            session.SwitchDeck(1);Assert.IsFalse(session.DeckCopy().Contains(12));
            session.SwitchDeck(-1);CollectionAssert.AreEqual(replacement,session.DeckCopy());
            session.SwitchDeck(-1);Assert.AreEqual(config.deckSlots-1,session.ActiveDeck);
            session.SwitchDeck(1);Assert.AreEqual(0,session.ActiveDeck);
        }
        [Test] public void PaidDrawUnlocksAndCountsDuplicatesAtSpecifiedCost()
        {
            long coins=session.Progress.Coins;int gems=session.Gems;
            Assert.AreEqual(0,session.Copies(12));
            for(int i=0;i<20;i++)Assert.AreEqual(12,Draw());
            Assert.AreEqual(20,session.Copies(12));Assert.AreEqual(1,session.Copies(0));
            Assert.AreEqual(coins,session.Progress.Coins);Assert.AreEqual(gems-1-19*5,session.Gems);
            Assert.AreEqual(13,session.FindSpirits(-1,0,0).Length);
        }
        [Test] public void FiltersOnlyShowOwnedMatchingSpiritsAndSortingIsStable()
        {
            Assert.IsEmpty(session.FindSpirits(-1,3,0));Draw();
            CollectionAssert.AreEqual(new[]{12},session.FindSpirits((int)Element.Fire,3,0));
            Assert.IsEmpty(session.FindSpirits((int)Element.Water,3,0));
            var elementOrder=session.FindSpirits(-1,0,1).Select(i=>config.catalog[i].element).ToArray();
            CollectionAssert.AreEqual(elementOrder.OrderBy(e=>e),elementOrder);
            Assert.AreEqual(12,session.FindSpirits(-1,0,2)[0]);
        }
        [Test] public void FirstSingleDiscountIsSharedAcrossAllBanners()
        {
            config.banners=new[]{config.banners[0],config.banners[0]};
            Assert.IsTrue(session.TryDraw(0,1,out var first,out _));Assert.AreEqual(1,first.Cost);
            Assert.IsTrue(session.TryDraw(1,1,out var second,out _));Assert.AreEqual(5,second.Cost);
            Assert.AreEqual(194,session.Gems);Assert.IsTrue(session.FirstSingleUsed);
        }
        [Test] public void TenDrawGrantsExactlyTenCopiesAndOneBonusWithoutUsingFirstSingle()
        {
            Assert.IsTrue(session.TryDraw(0,10,out var receipt,out _));
            Assert.AreEqual(10,receipt.SpiritIds.Count);Assert.AreEqual(10,session.Copies(12));
            Assert.AreEqual(50,receipt.Cost);Assert.AreEqual(1,receipt.Bonus);Assert.AreEqual(151,session.Gems);
            Assert.IsFalse(session.FirstSingleUsed);Assert.AreEqual(1,session.GachaCost(1));
            Assert.IsTrue(receipt.NewSpirits[0]);Assert.IsTrue(receipt.NewSpirits.Skip(1).All(x=>!x));
            Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<int>)receipt.SpiritIds)[0]=0);
        }
        [Test] public void TenDrawRequiresFullCostBeforeBonusAndFailedDrawPreservesInventory()
        {
            config.startingGems=49;session=new PlayerSession(config,tuning);
            Assert.IsFalse(session.TryDraw(0,10,out var receipt,out _));Assert.IsNull(receipt);
            Assert.AreEqual(49,session.Gems);Assert.AreEqual(0,session.Copies(12));Assert.IsFalse(session.FirstSingleUsed);
        }
        [TestCase(-1,1)] [TestCase(0,0)] [TestCase(0,2)] [TestCase(1,10)]
        public void InvalidGachaRequestDoesNotSpendOrConsumeDiscount(int banner,int count)
        {
            Assert.IsFalse(session.TryDraw(banner,count,out _,out _));
            Assert.AreEqual(200,session.Gems);Assert.AreEqual(0,session.Copies(12));Assert.IsFalse(session.FirstSingleUsed);
        }
        [Test] public void ExpiredBannerCannotConsumeFirstSingleDiscount()
        {
            config.banners[0].availableUntil="2000-01-01";
            Assert.IsFalse(session.TryDraw(0,1,out _,out _));Assert.AreEqual(200,session.Gems);Assert.IsFalse(session.FirstSingleUsed);
        }
        [Test] public void PresentationConfigurationRejectsInvalidReferencesAndPrices()
        {
            config.banners[0].featuredIndices=new[]{18};Assert.IsFalse(config.Validate(out _));
            config.banners[0].featuredIndices=new[]{12};config.singleGems=-1;Assert.IsFalse(config.Validate(out _));
        }
        [Test] public void UpgradeConsumesIncreasingCostAndMatchingMaterialsAtomically()
        {
            long before=session.Progress.Coins;int materials=session.MaterialCount(0);
            Assert.AreEqual(300,session.UpgradeCost(0,3));
            Assert.IsTrue(session.Upgrade(0,0,3));
            Assert.AreEqual(4,session.Level(0));Assert.AreEqual(before-300,session.Progress.Coins);
            Assert.AreEqual(materials-3,session.MaterialCount(0));
            Assert.AreEqual(config.catalog[0].baseHp+3*config.catalog[0].hpPerLevel,session.Hp(0));
            Assert.AreEqual(config.catalog[0].baseAttack+3*config.catalog[0].attackPerLevel,session.Attack(0));
            Assert.AreEqual(1,config.catalog[0].level);
            Assert.IsTrue(session.Upgrade(1,3,1));Assert.AreEqual(2,session.Level(1));
        }
        [TestCase(0,1,1)] [TestCase(0,0,0)] [TestCase(0,0,100)]
        [TestCase(12,0,1)] [TestCase(-1,0,1)] [TestCase(0,4,1)]
        public void InvalidUpgradeDoesNotSpendAnything(int id,int type,int count)
        {
            long coins=session.Progress.Coins;int materials=session.MaterialCount(0);
            Assert.IsFalse(session.Upgrade(id,type,count));
            Assert.AreEqual(coins,session.Progress.Coins);Assert.AreEqual(materials,session.MaterialCount(0));
            Assert.AreEqual(1,session.Level(0));
        }
        [Test] public void InsufficientFundsAndLevelCapRejectUpgrade()
        {
            config.maxSpiritLevel=2;Assert.IsFalse(session.Upgrade(0,0,2));
            Assert.IsTrue(session.Upgrade(0,0,1));Assert.IsFalse(session.Upgrade(0,0,1));
            session.Progress.TrySpendCoins(session.Progress.Coins);
            Assert.IsFalse(session.Upgrade(1,1,1));Assert.AreEqual(20,session.MaterialCount(1));
            Assert.IsFalse(session.Progress.TrySpendCoins(-1));Assert.AreEqual(0,session.Progress.Coins);
        }
        [Test] public void BattleReceivesSelectedDeckAndLevelsWithoutMutatingAssets()
        {
            Draw();int[] draft=session.DeckCopy();draft[0]=12;session.SaveDeck(draft);
            session.Upgrade(12,0,2);
            using(var loadout=new BattleLoadout(session,tuning))
            {
                Assert.AreEqual(3,loadout.Tuning.playerDeck[0].level);
                Assert.AreEqual(config.catalog[12].displayName,loadout.Tuning.playerDeck[0].displayName);
                Assert.AreEqual(session.DeckHp,new BattleState(loadout.Tuning).Player.MaxHp);
                Assert.AreEqual(1,config.catalog[12].level);
                Assert.AreNotSame(config.catalog[12],loadout.Tuning.playerDeck[0]);
                session.Upgrade(12,0,1);Assert.AreEqual(3,loadout.Tuning.playerDeck[0].level);
            }
            Assert.IsNotNull(config.catalog[12]);Assert.AreEqual(1,tuning.playerDeck[0].level);
        }
        [Test] public void EnergyIsReservedOncePerEntryAndNeverGoesNegative()
        {
            for(int i=0;i<config.maximumEnergy;i++)Assert.IsTrue(session.TryEnterBattle());
            Assert.AreEqual(0,session.Energy);Assert.IsFalse(session.TryEnterBattle());Assert.AreEqual(0,session.Energy);
        }
        [Test] public void InvalidPoolsFailBeforeSessionStarts()
        {
            config.banners[0].pool[1].weight=0;Assert.IsFalse(config.Validate(out _));
            Assert.Throws<ArgumentException>(()=>new PlayerSession(config,tuning));
            config.banners[0].pool[1].weight=1;config.banners[0].pool[1].spiritIndex=18;
            Assert.IsFalse(config.Validate(out _));
        }
    }
}

