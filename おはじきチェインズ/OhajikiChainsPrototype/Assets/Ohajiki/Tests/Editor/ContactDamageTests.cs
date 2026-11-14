using System;
using NUnit.Framework;
using UnityEngine;
namespace Ohajiki.Tests
{
    public class ContactDamageTests
    {
        BattleTuning tuning;
        SpiritDefinition spirit;
        [SetUp] public void Setup()
        {
            tuning=ScriptableObject.CreateInstance<BattleTuning>();
            spirit=ScriptableObject.CreateInstance<SpiritDefinition>();
            spirit.element=Element.Fire;spirit.baseHp=100;spirit.baseAttack=100;
            spirit.skill=Skill.Attack;spirit.skillPower=new[]{71f};
            for(int i=0;i<12;i++){tuning.playerDeck[i]=spirit;tuning.enemyDeck[i]=spirit;}
        }
        [TearDown] public void TearDown()
        {UnityEngine.Object.DestroyImmediate(tuning);UnityEngine.Object.DestroyImmediate(spirit);}
        [TestCase(false,0,false)] [TestCase(true,0,false)]
        [TestCase(false,30,false)] [TestCase(true,80,false)]
        [TestCase(false,.25f,true)] [TestCase(true,.5f,true)]
        public void ContactsArriveImmediatelyAndShareGuardWithSkill(bool enemy,float guard,bool percentage)
        {
            var state=new BattleState(tuning);var reference=new BattleState(tuning);
            if(enemy){state.Resolve(false,spirit,0,0,tuning);reference.Resolve(false,spirit,0,0,tuning);}
            var target=enemy?state.Player:state.Enemy;
            var referenceTarget=enemy?reference.Player:reference.Enemy;
            target.SetGuard(guard,2,percentage);referenceTarget.SetGuard(guard,2,percentage);
            int before=target.Hp;
            for(int contacts=1;contacts<=4;contacts++)
            {
                int raw=BattleRules.ContactDamage(spirit.Attack,contacts,spirit.element,target.Element,tuning);
                state.ApplyContactDamage(enemy,spirit,raw);
                Assert.AreEqual(before-target.MitigatedDamage(raw),target.Hp,"HP updates at each contact");
                Assert.AreEqual(2,target.GuardTurns,"contact does not consume a guard turn");
                Assert.IsFalse(state.ActionPending,"skill is not resolved during the shot");
            }
            int afterContacts=target.Hp;
            var normal=state.ResolveNormal(enemy,spirit,4,0,tuning);
            Assert.AreEqual(afterContacts,target.Hp,"zone/cut-in must not charge normal damage twice");
            Assert.AreEqual(before-afterContacts,normal.appliedNormalDamage);
            var full=state.ResolveSkill();
            var referenceFull=reference.Resolve(enemy,spirit,4,0,tuning);
            Assert.AreEqual(referenceTarget.Hp,target.Hp,"only delivery time changes");
            Assert.AreEqual(referenceFull.appliedDamage,full.appliedDamage);
            Assert.AreEqual(referenceFull.appliedSkillDamage,full.appliedSkillDamage);
            Assert.AreEqual(1,target.GuardTurns);
        }
        [Test] public void RepeatedCumulativeValueIsIdempotentAndNewActionResetsLedger()
        {
            var state=new BattleState(tuning);
            Assert.AreEqual(15,state.ApplyContactDamage(false,spirit,15));
            Assert.AreEqual(0,state.ApplyContactDamage(false,spirit,15));
            state.ResolveNormal(false,spirit,1,0,tuning);state.ResolveSkill();
            Assert.AreEqual(15,state.ApplyContactDamage(true,spirit,15));
            state.ResolveNormal(true,spirit,1,0,tuning);state.ResolveSkill();
            Assert.AreEqual(15,state.ApplyContactDamage(false,spirit,15));
        }
        [Test] public void WeightedContactIncreaseDoesNotRetroactivelyBoostEarlierHits()
        {
            var state=new BattleState(tuning);
            Assert.AreEqual(100,state.ApplyContactDamage(false,spirit,100));
            Assert.AreEqual(158,state.ApplyContactDamage(false,spirit,258));
            // 100 + 100 * 1.05 * 1.5 = 257.5, rounded once to 258.
            var normal=state.ResolveNormal(false,spirit,2,0,tuning,2.575);
            Assert.AreEqual(258,normal.appliedNormalDamage);
            Assert.AreEqual(942,state.Enemy.Hp);
        }
        [Test] public void LethalContactClampsHpAndRejectsFurtherDamage()
        {
            var state=new BattleState(tuning);
            state.Enemy.Receive(1190);
            Assert.AreEqual(10,state.ApplyContactDamage(false,spirit,15));
            Assert.IsTrue(state.Won);Assert.AreEqual(0,state.Enemy.Hp);
            Assert.Throws<InvalidOperationException>(()=>state.ApplyContactDamage(false,spirit,30));
            Assert.IsFalse(state.EnemyActsNext);
        }
        [Test] public void PracticeCanFinishTheShotAfterTargetReachesZero()
        {
            var state=new BattleState(tuning,true);state.Enemy.Receive(1190);
            state.ApplyContactDamage(false,spirit,15);
            Assert.AreEqual(0,state.ApplyContactDamage(false,spirit,30));
            var result=state.ResolveNormal(false,spirit,2,0,tuning);
            Assert.AreEqual(10,result.appliedNormalDamage);
            state.ResolveSkill();state.ContinuePractice();
            Assert.IsFalse(state.Finished);Assert.IsFalse(state.EnemyActsNext);
        }
        [Test] public void ZeroContactsStillResolvesSkillOnce()
        {
            var state=new BattleState(tuning);state.ResolveNormal(false,spirit,0,0,tuning);
            Assert.AreEqual(1200,state.Enemy.Hp);
            Assert.AreEqual(71,state.ResolveSkill().appliedSkillDamage);
            Assert.Throws<InvalidOperationException>(()=>state.ResolveSkill());
        }
        [Test] public void WrongSideAndDecreasingTotalsAreRejected()
        {
            var state=new BattleState(tuning);
            Assert.Throws<InvalidOperationException>(()=>state.ApplyContactDamage(true,spirit,15));
            state.ApplyContactDamage(false,spirit,30);
            Assert.Throws<ArgumentOutOfRangeException>(()=>state.ApplyContactDamage(false,spirit,15));
            state.ResolveNormal(false,spirit,2,0,tuning);
            Assert.Throws<InvalidOperationException>(()=>state.ApplyContactDamage(false,spirit,45));
        }
    }
}
