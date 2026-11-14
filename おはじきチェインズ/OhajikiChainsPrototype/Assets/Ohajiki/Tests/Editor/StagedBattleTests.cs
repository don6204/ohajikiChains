using System;
using NUnit.Framework;
using UnityEngine;
namespace Ohajiki.Tests
{
    public class StagedBattleTests
    {
        BattleTuning tuning;
        SpiritDefinition spirit;
        [SetUp] public void Setup()
        {
            tuning=ScriptableObject.CreateInstance<BattleTuning>();
            spirit=ScriptableObject.CreateInstance<SpiritDefinition>();
            spirit.element=Element.Fire;spirit.baseHp=100;spirit.baseAttack=100;spirit.skill=Skill.Attack;spirit.skillPower=new[]{71f};
            for(int i=0;i<12;i++){tuning.playerDeck[i]=spirit;tuning.enemyDeck[i]=spirit;}
        }
        [TearDown] public void TearDown(){UnityEngine.Object.DestroyImmediate(tuning);UnityEngine.Object.DestroyImmediate(spirit);}
        [Test] public void NormalLowersHpBeforeSkillAndTurnAdvancesOnlyAfterSkill()
        {
            var state=new BattleState(tuning);
            var normal=state.ResolveNormal(false,spirit,4,0,tuning);
            Assert.AreEqual(62,normal.appliedNormalDamage);Assert.AreEqual(1138,state.Enemy.Hp);
            Assert.IsTrue(state.ActionPending);Assert.IsFalse(state.EnemyActsNext);Assert.AreEqual(0,normal.appliedSkillDamage);
            var full=state.ResolveSkill();
            Assert.AreEqual(71,full.appliedSkillDamage);Assert.AreEqual(133,full.appliedDamage);Assert.AreEqual(1067,state.Enemy.Hp);
            Assert.IsFalse(state.ActionPending);Assert.IsTrue(state.EnemyActsNext);Assert.AreEqual(1,state.Turn);
            state.ResolveNormal(true,spirit,4,0,tuning);Assert.AreEqual(1,state.Turn);
            state.ResolveSkill();Assert.AreEqual(2,state.Turn);Assert.IsFalse(state.EnemyActsNext);
        }
        [TestCase(0,62,71)] [TestCase(30,32,71)] [TestCase(80,0,53)] [TestCase(200,0,0)]
        public void FixedGuardIsOneSharedBudget(int guard,int normalDamage,int skillDamage)
        {
            var state=new BattleState(tuning);state.Enemy.SetGuard(guard,2,false);
            var normal=state.ResolveNormal(false,spirit,4,0,tuning);
            Assert.AreEqual(normalDamage,normal.appliedNormalDamage);Assert.AreEqual(2,state.Enemy.GuardTurns);
            var full=state.ResolveSkill();Assert.AreEqual(skillDamage,full.appliedSkillDamage);Assert.AreEqual(1,state.Enemy.GuardTurns);
            Assert.AreEqual(normalDamage+skillDamage,full.appliedDamage);
        }
        [TestCase(0,133)] [TestCase(.25f,100)] [TestCase(.5f,66)] [TestCase(1,0)]
        public void PercentageGuardPreservesCombinedRounding(float guard,int total)
        {
            var state=new BattleState(tuning);state.Enemy.SetGuard(guard,1,true);
            var normal=state.ResolveNormal(false,spirit,4,0,tuning);
            Assert.AreEqual(1,state.Enemy.GuardTurns);
            var full=state.ResolveSkill();Assert.AreEqual(total,full.appliedDamage);
            Assert.AreEqual(total-normal.appliedDamage,full.appliedSkillDamage);Assert.AreEqual(0,state.Enemy.GuardTurns);
        }
        [TestCase(Skill.Heal,71)] [TestCase(Skill.PercentHeal,120)]
        public void HealingWaitsForSkill(Skill skill,int expected)
        {
            spirit.skill=skill;spirit.skillPower=new[]{skill==Skill.Heal?71f:.1f};
            var state=new BattleState(tuning);state.Player.Receive(300);
            state.ResolveNormal(false,spirit,4,0,tuning);Assert.AreEqual(900,state.Player.Hp);
            var full=state.ResolveSkill();Assert.AreEqual(expected,full.healing);Assert.AreEqual(900+expected,state.Player.Hp);
        }
        [TestCase(Skill.Guard)] [TestCase(Skill.PercentGuard)]
        public void GuardWaitsForSkill(Skill skill)
        {
            spirit.skill=skill;spirit.guardTurns=2;
            var state=new BattleState(tuning);state.ResolveNormal(false,spirit,4,0,tuning);
            Assert.AreEqual(0,state.Player.GuardTurns);
            Assert.IsTrue(state.ResolveSkill().guarding);Assert.AreEqual(2,state.Player.GuardTurns);
        }
        [Test] public void DuplicateAndOutOfOrderResolutionIsRejected()
        {
            var state=new BattleState(tuning);
            Assert.Throws<InvalidOperationException>(()=>state.ResolveSkill());
            state.ResolveNormal(false,spirit,4,0,tuning);
            Assert.Throws<InvalidOperationException>(()=>state.ResolveNormal(false,spirit,4,0,tuning));
            Assert.Throws<InvalidOperationException>(()=>state.ResolveNormal(true,spirit,4,0,tuning));
            state.ResolveSkill();int hp=state.Enemy.Hp;
            Assert.Throws<InvalidOperationException>(()=>state.ResolveSkill());Assert.AreEqual(hp,state.Enemy.Hp);
        }
        [Test] public void LethalNormalCompletesPresentationWithoutExtraHpLossOrEnemyTurn()
        {
            var state=new BattleState(tuning);state.ResolveNormal(false,spirit,1000,0,tuning);
            Assert.IsTrue(state.Won);Assert.IsTrue(state.ActionPending);
            var full=state.ResolveSkill();Assert.AreEqual(0,full.appliedSkillDamage);Assert.AreEqual(1200,full.appliedDamage);
            Assert.IsFalse(state.ActionPending);
            Assert.Throws<InvalidOperationException>(()=>state.ResolveNormal(true,spirit,4,0,tuning));
        }
        [Test] public void NoContactsStillResolvesPercentageSkill()
        {
            spirit.skill=Skill.PercentAttack;spirit.skillPower=new[]{.1f};
            var state=new BattleState(tuning);state.ResolveNormal(false,spirit,0,0,tuning);
            Assert.AreEqual(1200,state.Enemy.Hp);var full=state.ResolveSkill();
            Assert.AreEqual(120,full.appliedSkillDamage);Assert.AreEqual(1080,state.Enemy.Hp);
        }
    }
}
