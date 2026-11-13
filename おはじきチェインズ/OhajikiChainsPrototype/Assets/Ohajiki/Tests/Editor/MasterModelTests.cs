using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class MasterModelTests
    {
        HubConfig home;BattleTuning tuning;QuestCatalog quests;PlayerSession session;
        [SetUp] public void Setup()
        {
            home=ScriptableObject.CreateInstance<HubConfig>();tuning=ScriptableObject.CreateInstance<BattleTuning>();
            home.catalog=Enumerable.Range(0,18).Select(i=>{
                var s=ScriptableObject.CreateInstance<SpiritDefinition>();s.displayName="Spirit "+i;s.element=(Element)(i%3);return s;
            }).ToArray();
            home.banners=Array.Empty<GachaBanner>();home.startingGems=100;
            tuning.playerDeck=home.catalog.Take(12).ToArray();tuning.enemyDeck=home.catalog.Take(12).ToArray();
            quests=ScriptableObject.CreateInstance<QuestCatalog>();
            quests.quests=new[]{new QuestDefinition{id="master_quest",groupId="master_group",title="Master quest",experience=0,energyCost=0}};
            home.quests=quests;session=new PlayerSession(home,tuning);
        }
        [TearDown] public void Cleanup()
        {
            foreach(var spirit in home.catalog)UnityEngine.Object.DestroyImmediate(spirit);
            UnityEngine.Object.DestroyImmediate(quests);UnityEngine.Object.DestroyImmediate(home);UnityEngine.Object.DestroyImmediate(tuning);
        }
        QuestRun Begin()
        {Assert.IsTrue(session.TryBeginQuest("master_quest",out var run,out string error),error);return run;}
        [Test] public void CleanseHasNoMagnitudeAndKeepsNullWhileOtherSkillsRequirePower()
        {
            var s=home.catalog[0];s.skill=Skill.Cleanse;s.skillPower=null;s.portrait=null;
            Assert.IsTrue(s.IsValid);Assert.AreEqual(0,s.Power(5));Assert.IsNull(s.skillPower);Assert.IsNull(s.portrait);
            s.skill=Skill.Attack;Assert.IsFalse(s.IsValid);
        }
        [Test] public void CharacterSpecificExperienceAndLevelCapApplyToRewardsAndUpgrades()
        {
            var s=home.catalog[0];s.maxLevel=3;s.experienceBase=10;s.experiencePerLevel=5;
            session=new PlayerSession(home,tuning);
            Assert.AreEqual(10,session.ExperienceToNextLevel(0));
            session.AwardBattleExperience(new[]{0},10);Assert.AreEqual(2,session.Level(0));Assert.AreEqual(15,session.ExperienceToNextLevel(0));
            session.AwardBattleExperience(new[]{0},100);Assert.AreEqual(3,session.Level(0));Assert.AreEqual(0,session.ExperienceToNextLevel(0));
            Assert.IsFalse(session.CanUpgrade(0,0,1));Assert.AreEqual(1,s.level);
            Assert.AreEqual(home.spiritExperienceBase,session.ExperienceToNextLevel(1));
        }
        [Test] public void NpcDeckPreservesIndividualLevelsWithoutMutatingSources()
        {
            var q=quests.quests[0];q.enemyDeck=home.catalog.Take(12).ToArray();q.preserveEnemyLevels=true;q.enemyLevel=1;
            for(int i=0;i<12;i++)q.enemyDeck[i].level=i+1;
            using(var battle=new BattleLoadout(session,tuning,q))
                for(int i=0;i<12;i++)
                {
                    Assert.AreEqual(i+1,battle.Tuning.enemyDeck[i].level);
                    Assert.AreEqual(q.enemyDeck[i].Hp,battle.Tuning.enemyDeck[i].Hp);
                    Assert.AreNotSame(q.enemyDeck[i],battle.Tuning.enemyDeck[i]);
                }
            Assert.AreEqual(12,q.enemyDeck[11].level);Assert.AreEqual(10,q.enemyDeck[11].hpPerLevel);
        }
        [Test] public void MasterRewardsAreSnapshottedAtomicAndFirstSpiritDoesNotDoubleDrop()
        {
            var q=quests.quests[0];q.useMasterRewards=true;q.firstClearGems=1;q.firstClearSpirit=true;q.rewardSpiritIndex=12;
            q.masterRewards=new[]{
                new QuestRewardEntry{id="medals",itemKind=ShopRewardKind.Medals,min=17,max=17},
                new QuestRewardEntry{id="material",itemKind=ShopRewardKind.FirePower,min=3,max=3},
                new QuestRewardEntry{id="spirit",spiritIndex=12,probability=1,min=3,max=3}
            };
            Assert.IsTrue(quests.Validate(home.catalog.Length,out string error),error);
            long coins=session.Progress.Coins;int material=session.MaterialCount(0),gems=session.Gems;
            var run=Begin();q.masterRewards[0].min=q.masterRewards[0].max=999;
            var receipt=session.CompleteQuest(run,true,tuning);
            Assert.AreEqual(coins+17,session.Progress.Coins);Assert.AreEqual(material+3,session.MaterialCount(0));
            Assert.AreEqual(gems+1,session.Gems);Assert.AreEqual(1,session.Copies(12));Assert.IsTrue(receipt.FirstClear);
            Assert.IsNull(session.CompleteQuest(run,true,tuning));Assert.AreEqual(1,session.Copies(12));
            session.CompleteQuest(Begin(),true,tuning);Assert.AreEqual(4,session.Copies(12));Assert.AreEqual(gems+1,session.Gems);
        }
        [Test] public void RewardOverflowCommitsNeitherItemsNorSpiritAndKeepsRun()
        {
            home.startingGems=int.MaxValue;session=new PlayerSession(home,tuning);
            quests.quests[0].useMasterRewards=true;
            quests.quests[0].masterRewards=new[]{
                new QuestRewardEntry{id="medals",itemKind=ShopRewardKind.Medals,min=123,max=123},
                new QuestRewardEntry{id="gems",itemKind=ShopRewardKind.Gems},
                new QuestRewardEntry{id="spirit",spiritIndex=12}
            };
            var run=Begin();long coins=session.Progress.Coins;
            Assert.Throws<InvalidOperationException>(()=>session.CompleteQuest(run,true,tuning));
            Assert.AreEqual(coins,session.Progress.Coins);Assert.AreEqual(0,session.Copies(12));
            Assert.AreEqual(int.MaxValue,session.Gems);Assert.AreSame(run,session.ActiveQuestRun);Assert.IsFalse(session.IsCleared(run.Quest.id));
        }
        [Test] public void ZeroChanceNeverGrantsAndRandomQuantityStaysWithinInclusiveBounds()
        {
            quests.quests[0].useMasterRewards=true;
            quests.quests[0].masterRewards=new[]{
                new QuestRewardEntry{id="absent",spiritIndex=12,probability=0},
                new QuestRewardEntry{id="material",itemKind=ShopRewardKind.WaterPower,min=2,max=4}
            };
            for(int i=0;i<30;i++)
            {
                int before=session.MaterialCount(1);session.CompleteQuest(Begin(),true,tuning);
                Assert.That(session.MaterialCount(1)-before,Is.InRange(2,4));Assert.AreEqual(0,session.Copies(12));
            }
        }
        [TestCase(false,99)] [TestCase(true,0)]
        public void EmptyMasterArrayUsesExplicitModeAcrossRunSerialization(bool useMaster,int expectedMedals)
        {
            var q=quests.quests[0];q.medals=99;q.useMasterRewards=useMaster;q.masterRewards=Array.Empty<QuestRewardEntry>();
            long coins=session.Progress.Coins;session.CompleteQuest(Begin(),true,tuning);
            Assert.AreEqual(coins+expectedMedals,session.Progress.Coins);
        }
        [Test] public void NullPortraitWidgetIsHiddenWithoutCreatingSubstituteArt()
        {
            var root=new GameObject("Portrait test");
            try
            {
                new HubWidgets(null).Portrait(root.transform,null,0,0,50,50);
                var image=root.GetComponentInChildren<UnityEngine.UI.Image>();
                Assert.IsNull(image.sprite);Assert.IsFalse(image.enabled);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
