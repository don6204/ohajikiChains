using System;
using NUnit.Framework;
using UnityEngine;
namespace Ohajiki.Tests
{
    public class DeferredDefeatTests
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
        [TestCase(false)] [TestCase(true)]
        public void LethalContactAllowsRemainingHitsAndSkillButNoNewShot(bool enemy)
        {
            var state=new BattleState(tuning);
            if(enemy)state.Resolve(false,spirit,0,0,tuning);
            var target=enemy?state.Player:state.Enemy;
            target.Receive(target.MaxHp-10);
            state.BeginShot(enemy,spirit);
            Assert.AreEqual(10,state.ApplyContactDamage(enemy,spirit,15));
            Assert.IsTrue(state.Finished);Assert.IsTrue(state.ShotInProgress);
            Assert.AreEqual(!enemy,state.Won);
            Assert.AreEqual(0,state.ApplyContactDamage(enemy,spirit,30));
            Assert.AreEqual(0,target.Hp);
            var normal=state.ResolveNormal(enemy,spirit,2,0,tuning);
            Assert.AreEqual(10,normal.appliedNormalDamage);
            Assert.IsTrue(state.ActionPending);Assert.IsTrue(state.ShotInProgress);
            var full=state.ResolveSkill();
            Assert.AreEqual(10,full.appliedDamage);Assert.AreEqual(0,full.appliedSkillDamage);
            Assert.IsFalse(state.ActionPending);Assert.IsFalse(state.ShotInProgress);
            Assert.Throws<InvalidOperationException>(()=>state.BeginShot(!enemy,spirit));
            Assert.Throws<InvalidOperationException>(()=>state.ResolveNormal(!enemy,spirit,1,0,tuning));
        }
        [TestCase(false)] [TestCase(true)]
        public void FirstDefeatIsPreservedIfBothSidesReachZeroBeforeTurnEnds(bool enemyFirst)
        {
            var state=new BattleState(tuning);state.BeginShot(false,spirit);
            var first=enemyFirst?state.Enemy:state.Player;
            var second=enemyFirst?state.Player:state.Enemy;
            first.ReceiveTrap(first.MaxHp);state.RecordDefeat();
            second.ReceiveTrap(second.MaxHp);state.RecordDefeat();
            Assert.AreEqual(enemyFirst,state.Won);
            state.ApplyContactDamage(false,spirit,15);
            state.ResolveNormal(false,spirit,1,0,tuning);state.ResolveSkill();
            Assert.IsTrue(state.Finished);Assert.AreEqual(enemyFirst,state.Won);
        }
        [Test] public void LethalTrapBeforeAnyContactStillAllowsTheLaunchedShotToResolve()
        {
            var state=new BattleState(tuning);state.BeginShot(false,spirit);
            state.Player.ReceiveTrap(state.Player.MaxHp);state.RecordDefeat();
            state.ResolveNormal(false,spirit,0,0,tuning);
            Assert.IsTrue(state.ActionPending);
            Assert.AreEqual(71,state.ResolveSkill().appliedSkillDamage);
            Assert.IsFalse(state.Won);Assert.IsTrue(state.Finished);
        }
        [Test] public void HealingSkillCannotReverseRecordedDefeat()
        {
            spirit.skill=Skill.Heal;
            var state=new BattleState(tuning);state.BeginShot(false,spirit);
            state.Player.ReceiveTrap(state.Player.MaxHp);state.RecordDefeat();
            state.ResolveNormal(false,spirit,0,0,tuning);state.ResolveSkill();
            Assert.IsTrue(state.Finished);Assert.IsFalse(state.Won);
        }
        [Test] public void WrongSideAndUnlaunchedShotsCannotBypassDefeat()
        {
            var state=new BattleState(tuning);state.BeginShot(false,spirit);
            Assert.Throws<InvalidOperationException>(()=>state.BeginShot(false,spirit));
            state.Enemy.Receive(state.Enemy.MaxHp);state.RecordDefeat();
            Assert.Throws<InvalidOperationException>(()=>state.ApplyContactDamage(true,spirit,15));
            Assert.Throws<InvalidOperationException>(()=>state.ResolveNormal(true,spirit,1,0,tuning));
            var fresh=new BattleState(tuning);fresh.Enemy.Receive(fresh.Enemy.MaxHp);
            Assert.Throws<InvalidOperationException>(()=>fresh.BeginShot(false,spirit));
            Assert.Throws<InvalidOperationException>(()=>fresh.ResolveNormal(false,spirit,1,0,tuning));
        }
        [Test] public void PracticeZeroHpStillAllowsAnotherPlayerTurn()
        {
            var state=new BattleState(tuning,true);state.BeginShot(false,spirit);
            state.ApplyContactDamage(false,spirit,1200);
            state.ResolveNormal(false,spirit,80,0,tuning);state.ResolveSkill();
            Assert.IsFalse(state.Finished);Assert.IsTrue(state.Won);
            state.ContinuePractice();state.BeginShot(false,spirit);
            Assert.IsTrue(state.ShotInProgress);
        }
    }
}
