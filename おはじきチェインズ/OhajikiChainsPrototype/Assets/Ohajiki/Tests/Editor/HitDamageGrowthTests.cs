using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class HitDamageGrowthTests
    {
        BattleTuning tuning;
        SpiritDefinition spirit;
        [SetUp] public void Setup()
        {
            tuning=ScriptableObject.CreateInstance<BattleTuning>();
            tuning.contactAttackFactor=.05f;tuning.deceleration=0;tuning.maximumShotSeconds=30;
            spirit=ScriptableObject.CreateInstance<SpiritDefinition>();
            spirit.element=Element.Fire;spirit.baseHp=1000;spirit.baseAttack=100;spirit.baseSpeed=600;
            spirit.skill=Skill.Attack;spirit.skillPower=new[]{71f};
            for(int i=0;i<12;i++){tuning.playerDeck[i]=spirit;tuning.enemyDeck[i]=spirit;}
        }
        [TearDown] public void Cleanup()
        {Object.DestroyImmediate(spirit);Object.DestroyImmediate(tuning);}

        [TestCase(false)] [TestCase(true)]
        public void PhysicalContactsAccumulateGrowingDamageAndApplyTogetherAtShotEnd(bool enemy)
        {
            var sim=new ShotSimulation(tuning);var state=new BattleState(tuning);
            if(enemy)state.Resolve(false,spirit,0,0,tuning);
            var target=enemy?state.Player:state.Enemy;
            int before=target.Hp;
            foreach(var charge in sim.Charges)charge.Active=false;
            sim.Prepare(enemy,312);state.BeginShot(enemy,spirit);
            Assert.IsTrue(sim.Launch((enemy?Vector2.down:Vector2.up)*300,spirit));
            int displayedDamage=0;
            sim.CollectedCharge+=(index,matched)=>displayedDamage=
                BattleRules.ContactDamage(spirit.Attack,sim.DamageWeight,spirit.element,target.Element,tuning);
            double[] weights={1,2.05,3.15,4.30};
            int[] cumulative={100,205,315,430};
            for(int i=0;i<weights.Length;i++)
            {
                var charge=sim.Charges[i];charge.Active=true;charge.Special=SpecialCharge.None;
                charge.Element=spirit.element;charge.Position=sim.Position+sim.Velocity.normalized*5;
                sim.Tick(.0001f);
                Assert.AreEqual(i+1,sim.Collected);
                Assert.AreEqual(weights[i],sim.DamageWeight,1e-6);
                Assert.AreEqual(cumulative[i],displayedDamage,"each contact increases the damage display");
                Assert.AreEqual(before,target.Hp,"normal damage is held until shot end");
            }
            sim.CancelShot();
            state.ResolveNormal(enemy,spirit,sim.Collected,0,tuning,sim.DamageWeight);
            Assert.AreEqual(before-430,target.Hp,"the complete normal damage is applied once at shot end");
            Assert.AreEqual(71,state.ResolveSkill().appliedSkillDamage,"hit growth does not multiply skills");
            Assert.AreEqual(before-501,target.Hp);
        }

        [Test] public void SplitProjectilesShareOneHitIndexAndRefillContinuesIt()
        {
            var sim=new ShotSimulation(tuning);
            foreach(var charge in sim.Charges)charge.Active=false;
            sim.Prepare(false,312);Assert.IsTrue(sim.Launch(Vector2.up*300,spirit));
            var split=sim.Charges[0];split.Active=true;split.Special=SpecialCharge.Split;
            split.Position=sim.Position+Vector2.up*5;sim.Tick(.0001f);
            Assert.AreEqual(3,sim.Projectiles.Count);Assert.AreEqual(1,sim.Collected);
            sim.Tick(.2f);sim.ResetBoard();
            foreach(var charge in sim.Charges)charge.Active=false;
            var clone=sim.Projectiles[1];var next=sim.Charges[1];
            next.Active=true;next.Special=SpecialCharge.None;next.Position=clone.Position+clone.Velocity.normalized*5;
            Assert.Greater(Vector2.Distance(next.Position,sim.Position),tuning.shotRadius+tuning.chargeRadius);
            sim.Tick(.0001f);
            Assert.AreEqual(2,sim.Collected);Assert.AreEqual(2.05,sim.DamageWeight,1e-6);
        }

        [TestCase(-1,0)] [TestCase(0,0)] [TestCase(1,100)]
        [TestCase(2,205)] [TestCase(3,315)] [TestCase(4,430)]
        public void CountOnlyCalculationMatchesIndividualGrowingContacts(int hits,int expected)
        {Assert.AreEqual(expected,BattleRules.ContactDamage(100,hits,Element.Fire,Element.Fire,tuning));}

        [TestCase(100000)] [TestCase(int.MaxValue)]
        public void VeryLongComboSaturatesWithoutNegativeDamageOrOverflow(int hits)
        {
            Assert.AreEqual(int.MaxValue,BattleRules.ContactDamage(100,hits,Element.Fire,Element.Fire,tuning));
            Assert.AreEqual(0,BattleRules.ContactDamage(0,hits,Element.Fire,Element.Fire,tuning));
            var state=new BattleState(tuning);state.BeginShot(false,spirit);
            var normal=state.ResolveNormal(false,spirit,hits,0,tuning);
            Assert.AreEqual(int.MaxValue,normal.normalAfterGuard);
            Assert.GreaterOrEqual(state.ResolveSkill().appliedSkillDamage,0);
            Assert.AreEqual(0,state.Enemy.Hp);Assert.IsTrue(state.Won);
        }
    }
}
