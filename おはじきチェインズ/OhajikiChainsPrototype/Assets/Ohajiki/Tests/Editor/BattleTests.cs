using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace Ohajiki.Tests
{
    public class BattleTests
    {
        BattleTuning tuning;
        [SetUp] public void Setup()
        {
            tuning=ScriptableObject.CreateInstance<BattleTuning>();
            for(int i=0;i<12;i++)
            {
                var spirit=ScriptableObject.CreateInstance<SpiritDefinition>();
                spirit.element=(Element)(i%3); spirit.baseHp=100+i; spirit.skill=Skill.Attack;
                tuning.playerDeck[i]=spirit;
                tuning.enemyDeck[i]=UnityEngine.Object.Instantiate(spirit);
            }
        }
        [TearDown] public void TearDown()
        {
            foreach(var s in tuning.playerDeck) if(s != null) UnityEngine.Object.DestroyImmediate(s);
            foreach(var s in tuning.enemyDeck) if(s != null) UnityEngine.Object.DestroyImmediate(s);
            UnityEngine.Object.DestroyImmediate(tuning);
        }
        [TestCase(0,1)] [TestCase(1,2)] [TestCase(2,2)] [TestCase(3,3)] [TestCase(4,3)]
        [TestCase(5,4)] [TestCase(9,4)] [TestCase(10,5)] [TestCase(30,5)]
        public void EffectLevelMatchesSpec(int count,int expected) => Assert.AreEqual(expected,BattleRules.EffectLevel(count,tuning));
        [Test] public void InspectorValuesDriveRules()
        {
            tuning.effectThresholds=new[]{2,4}; tuning.contactAttackFactor=.5f; tuning.advantage=2;
            Assert.AreEqual(1,BattleRules.EffectLevel(1,tuning));
            Assert.AreEqual(3,BattleRules.EffectLevel(4,tuning));
            Assert.AreEqual(400,BattleRules.ContactDamage(100,4,Element.Fire,Element.Wood,tuning));
        }
        [Test] public void AffinityMatchesAllPairs()
        {
            float[,] expected={{1,.8f,1.2f,1},{1.2f,1,.8f,1},{.8f,1.2f,1,1},{1,1,1,1}};
            for(int a=0;a<4;a++) for(int b=0;b<4;b++)
                Assert.AreEqual(expected[a,b],BattleRules.Affinity((Element)a,(Element)b,tuning),.0001f);
        }
        [TestCase(0,0)] [TestCase(1,30)] [TestCase(4,120)] [TestCase(10,300)]
        public void NormalAttackIsAttackTimesContactsTimesPoint15(int contacts,int expected) =>
            Assert.AreEqual(expected,BattleRules.ContactDamage(200,contacts,Element.Fire,Element.Fire,tuning));
        [Test] public void DeckHoldsOtherThreeAndDoesNotRepeatBeforeExhaustion()
        {
            var deck=new SpiritDeck(17); var seen=Enumerable.Range(0,4).Select(deck.CardAt).ToList();
            for(int i=0;i<8;i++) { deck.Replace(0); seen.Add(deck.CardAt(0)); }
            Assert.AreEqual(12,seen.Distinct().Count());
            int[] retained=Enumerable.Range(1,3).Select(deck.CardAt).ToArray();
            for(int i=0;i<100;i++)
            {
                deck.Replace(0);
                CollectionAssert.AreEqual(retained,Enumerable.Range(1,3).Select(deck.CardAt));
                Assert.AreEqual(4,Enumerable.Range(0,4).Select(deck.CardAt).Distinct().Count());
            }
        }
        [Test] public void HpIsSumOfTwelveIncludingGrowth()
        {
            tuning.playerDeck[0].level=3;
            var state=new BattleState(tuning);
            Assert.AreEqual(1286,state.Player.MaxHp);
            Assert.AreEqual(1266,state.Enemy.MaxHp);
        }
        [Test] public void NoAutomaticRetaliationAndActionsAlternate()
        {
            var state=new BattleState(tuning);
            var result=state.Resolve(false,tuning.playerDeck[0],2,0,tuning);
            Assert.Greater(result.normalDamage,0); Assert.Greater(result.skillDamage,0);
            Assert.AreEqual(state.Player.MaxHp,state.Player.Hp); Assert.IsTrue(state.EnemyActsNext);
            Assert.Throws<InvalidOperationException>(()=>state.Resolve(false,tuning.playerDeck[0],0,0,tuning));
            state.Resolve(true,tuning.enemyDeck[0],2,0,tuning);
            Assert.Less(state.Player.Hp,state.Player.MaxHp); Assert.AreEqual(2,state.Turn);
        }
        [Test] public void VictoryBlocksEnemyAction()
        {
            var state=new BattleState(tuning);
            tuning.playerDeck[0].skillPower=new[]{100000f};
            state.Resolve(false,tuning.playerDeck[0],0,0,tuning);
            Assert.IsTrue(state.Won); Assert.AreEqual(state.Player.MaxHp,state.Player.Hp);
            Assert.Throws<InvalidOperationException>(()=>state.Resolve(true,tuning.enemyDeck[0],0,0,tuning));
        }
        [Test] public void EnemyCanDefeatPlayer()
        {
            var state=new BattleState(tuning);
            tuning.enemyDeck[0].skillPower=new[]{100000f};
            state.Resolve(false,tuning.playerDeck[0],0,0,tuning);
            state.Resolve(true,tuning.enemyDeck[0],0,0,tuning);
            Assert.IsTrue(state.Finished); Assert.IsFalse(state.Won); Assert.AreEqual(0,state.Player.Hp);
        }
        [Test] public void FixedAndPercentageGuardLastConfiguredNumberOfEnemyActions()
        {
            var target=new Combatant(1000,Element.Fire);
            target.SetGuard(50,2,false);
            Assert.AreEqual(50,target.Receive(100)); Assert.AreEqual(50,target.Receive(100));
            Assert.AreEqual(100,target.Receive(100)); Assert.AreEqual(0,target.GuardTurns);
            target.SetGuard(.25f,1,true); Assert.AreEqual(75,target.Receive(100));
        }
        [Test] public void RecoveryClampsAndPercentageUsesMaxHp()
        {
            var state=new BattleState(tuning);
            state.Player.Receive(300); var spirit=tuning.playerDeck[0];
            spirit.skill=Skill.PercentHeal; spirit.skillPower=new[]{.1f};
            var result=state.Resolve(false,spirit,0,0,tuning);
            Assert.AreEqual(127,result.healing);
            Assert.AreEqual(0,result.normalDamage); Assert.AreEqual(0,result.skillDamage);
            Assert.AreEqual(173,state.Player.Heal(10000));
        }
        [Test] public void NeutralIsForbiddenForSpiritButKeptInBoard()
        {
            Assert.IsTrue(tuning.Validate(out _));
            tuning.playerDeck[0].element=Element.Neutral;
            Assert.IsFalse(tuning.Validate(out _));
            tuning.elementWeights=new Vector4(0,0,0,1);
            var sim=new ShotSimulation(tuning);
            Assert.IsTrue(sim.Charges.All(c=>c.Element==Element.Neutral));
        }
        [Test] public void BothSidesReflectAndStopInsideBoard()
        {
            foreach(bool enemy in new[]{false,true})
            {
                var sim=new ShotSimulation(tuning);
                foreach(var c in sim.Charges) c.Active=false;
                sim.Prepare(enemy,312);
                var direction=new Vector2(900,enemy?-100:100);
                Assert.IsFalse(sim.Launch(-direction,tuning.playerDeck[0]));
                Assert.IsTrue(sim.Launch(direction,tuning.playerDeck[0]));
                bool bounced=false;
                for(int i=0;i<1000 && sim.Moving;i++)
                {
                    sim.Tick(1f/60); bounced|=sim.Velocity.x<0;
                    Assert.That(sim.Position.x,Is.InRange(tuning.shotRadius,ShotSimulation.Width-tuning.shotRadius));
                    Assert.That(sim.Position.y,Is.InRange(tuning.shotRadius,ShotSimulation.Height-tuning.shotRadius));
                }
                Assert.IsTrue(bounced); Assert.IsFalse(sim.Moving);
            }
        }
        [TestCase(Element.Fire,1)] [TestCase(Element.Water,0)] [TestCase(Element.Neutral,0)]
        public void AllContactsCountButOnlySameElementChargesSkill(Element element,int expected)
        {
            var sim=new ShotSimulation(tuning); foreach(var c in sim.Charges)c.Active=false;
            sim.Charges[0].Active=true; sim.Charges[0].Element=element;
            sim.Charges[0].Position=new Vector2(312,200);
            sim.Launch(Vector2.up*100,tuning.playerDeck[0]);
            for(int i=0;i<1000 && sim.Moving;i++)sim.Tick(1f/30);
            Assert.AreEqual(1,sim.Collected); Assert.AreEqual(expected,sim.Matched);
        }
        [Test] public void ResetReplacesEvenUntouchedCharges()
        {
            var sim=new ShotSimulation(tuning);
            foreach(var c in sim.Charges)c.Element=Element.Neutral;
            tuning.elementWeights=new Vector4(1,0,0,0);
            sim.ResetBoard();
            Assert.IsTrue(sim.Charges.All(c=>c.Active && c.Element==Element.Fire));
            Assert.AreEqual(2,sim.ResetCount);
        }
        [Test] public void EnemyDirectionDoesNotDependOnPlayerSwipeSensitivity()
        {
            tuning.minimumSwipe=1000; tuning.minimumForwardSwipe=900;
            var player=new ShotSimulation(tuning);
            Assert.IsFalse(player.Launch(Vector2.up*300,tuning.playerDeck[0]));
            var enemy=new ShotSimulation(tuning); enemy.Prepare(true,312);
            Assert.IsFalse(enemy.Launch(Vector2.zero,tuning.enemyDeck[0]));
            Assert.IsFalse(enemy.Launch(Vector2.up*300,tuning.enemyDeck[0]));
            Assert.IsTrue(enemy.Launch(Vector2.down*300,tuning.enemyDeck[0]));
            Assert.Less(enemy.Velocity.y,0);
        }
        [Test] public void IncompleteAuthoredDeckIsRejectedWithoutReplacingRemainingCards()
        {
            var original=tuning.playerDeck.ToArray();
            var retained=tuning.playerDeck.Skip(1).ToArray();
            tuning.playerDeck[0]=null;
            try
            {
                // Exercise the actual Editor bootstrap path without loading/saving a scene.
                var builder=AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a=>a.GetType("PrototypeBuilder")).First(t=>t != null);
                var configure=builder.GetMethod("ConfigureDeck",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                configure.Invoke(null,new object[]{tuning,new Sprite[3]});
                Assert.IsNull(tuning.playerDeck[0]);
                CollectionAssert.AreEqual(retained,tuning.playerDeck.Skip(1));
                Assert.IsFalse(tuning.Validate(out _));
            }
            finally { tuning.playerDeck=original; }
        }
        [TestCase(1,100)] [TestCase(2,550)] [TestCase(9,900)] [TestCase(10,1200)]
        [TestCase(19,1740)] [TestCase(20,2300)] [TestCase(29,2975)] [TestCase(30,4000)] [TestCase(49,5900)]
        public void RankTableTransitionsUseCurrentRank(int target,int required)
        {
            var p=new PlayerProgress();
            while(p.Rank<target)p.Award(p.Needed(tuning),0,tuning);
            Assert.AreEqual(required,p.Needed(tuning));
        }
        [Test] public void RewardsAccumulateAndCapCanBeExtended()
        {
            var p=new PlayerProgress(); p.Award(50,500,tuning); p.Award(50,500,tuning);
            Assert.AreEqual(2,p.Rank); Assert.AreEqual(1000,p.Coins); Assert.AreEqual(100,p.TotalExperience);
            p.Award(1000000,0,tuning); Assert.AreEqual(50,p.Rank);
            tuning.maxRank=60;
            tuning.rankBands=tuning.rankBands.Concat(new[]{new RankBand(60,1000,100)}).ToArray();
            p.Award(0,0,tuning); Assert.AreEqual(60,p.Rank);
        }
    }
}
