using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public class BoardRulesTests
    {
        BattleTuning tuning;
        SpiritDefinition spirit;
        [SetUp] public void Setup()
        {
            tuning=Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<BattleTuning>("Assets/Ohajiki/BattleTuning.asset"));
            tuning.deceleration=1;tuning.maximumShotSeconds=30;tuning.stopSpeed=1;
            tuning.specialChargeProbabilities=new float[6];
            spirit=ScriptableObject.CreateInstance<SpiritDefinition>();spirit.baseSpeed=600;
        }
        [TearDown] public void Cleanup(){Object.DestroyImmediate(spirit);Object.DestroyImmediate(tuning);}
        ShotSimulation ClearBoard(bool enemy=false)
        {
            var sim=new ShotSimulation(tuning);
            foreach(var charge in sim.Charges){charge.Special=SpecialCharge.VerticalMissile;charge.Element=spirit.element;}
            sim.Charges[enemy?56:0].Special=SpecialCharge.HorizontalMissile;
            sim.Charges[enemy?49:9].Special=SpecialCharge.HorizontalMissile;
            sim.Prepare(enemy,52);
            Assert.IsTrue(sim.Launch((enemy?Vector2.down:Vector2.up)*300,spirit));
            for(int i=0;i<100&&sim.AllClearCount==0;i++)sim.Tick(.001f);
            Assert.AreEqual(1,sim.AllClearCount);
            return sim;
        }
        [Test] public void AuthoredBoardHas64NonOverlappingCellsInsideBothLaunchLines()
        {
            Assert.AreEqual(8,tuning.boardRows);Assert.AreEqual(8,tuning.boardColumns);
            var sim=new ShotSimulation(tuning);Assert.AreEqual(64,sim.Charges.Length);
            Assert.AreEqual(64,sim.Charges.Select(c=>c.Position).Distinct().Count());
            float diameter=tuning.chargeRadius*2;
            for(int i=0;i<64;i++)for(int j=i+1;j<64;j++)
            {
                var delta=sim.Charges[i].Position-sim.Charges[j].Position;
                Assert.IsTrue(Mathf.Abs(delta.x)>=diameter||Mathf.Abs(delta.y)>=diameter,"Charge cells overlap");
            }
            Assert.Greater(sim.Charges.Min(c=>c.Position.y)-tuning.chargeRadius,ShotSimulation.Height*tuning.launchInset+tuning.shotRadius);
            Assert.Less(sim.Charges.Max(c=>c.Position.y)+tuning.chargeRadius,ShotSimulation.Height*(1-tuning.launchInset)-tuning.shotRadius);
            // The user's shot area extends beyond the ball footprint: the input strip is +/-42.
            // Include the decorative rim and a visible gap, not just the collision radius.
            float rimRadius=tuning.chargeRadius*56f/54f;
            Assert.Greater(sim.Charges.Min(c=>c.Position.y)-rimRadius,ShotSimulation.Height*tuning.launchInset+42+8);
            Assert.Less(sim.Charges.Max(c=>c.Position.y)+rimRadius,548-8);
        }
        [TestCase(false)] [TestCase(true)]
        public void MissileCascadeClears64OnlyOnceForEitherSide(bool enemy)
        {
            var sim=ClearBoard(enemy);Assert.IsTrue(sim.Charges.All(c=>!c.Active));
            Assert.AreEqual(64,sim.Collected);Assert.AreEqual(64,sim.Matched);Assert.AreEqual(64,sim.WeightedContacts);
            sim.Tick(.1f);Assert.AreEqual(1,sim.AllClearCount);Assert.AreEqual(1,sim.ResetCount);
        }
        [Test] public void MovingShotRefillsAfterFiveSecondsWithoutLosingItsAccumulatedAttack()
        {
            var sim=ClearBoard();sim.Tick(4.98f);
            Assert.AreEqual(1,sim.ResetCount);Assert.IsTrue(sim.AwaitingAllClearRefill);
            float speed=sim.Velocity.magnitude;
            sim.Tick(.04f);
            Assert.AreEqual(2,sim.ResetCount);Assert.IsTrue(sim.Moving);Assert.IsFalse(sim.AwaitingAllClearRefill);
            Assert.IsTrue(sim.Charges.Any(c=>c.Active));
            Assert.GreaterOrEqual(sim.Collected,64);Assert.GreaterOrEqual(sim.Matched,64);Assert.GreaterOrEqual(sim.WeightedContacts,64);
            Assert.That(sim.Velocity.magnitude,Is.InRange(speed-.05f,speed+.001f));
        }
        [Test] public void AnotherAllClearStartsAFreshFiveSecondWaitInTheSameShot()
        {
            var sim=ClearBoard();sim.Tick(5.02f);Assert.AreEqual(2,sim.ResetCount);
            foreach(var charge in sim.Charges)charge.Active=false;
            var last=sim.Charges[0];last.Active=true;last.Special=SpecialCharge.None;
            last.Position=sim.Position+sim.Velocity.normalized*2;
            sim.Tick(.001f);Assert.AreEqual(2,sim.AllClearCount);
            sim.Tick(4.98f);Assert.AreEqual(2,sim.ResetCount);
            sim.Tick(.04f);Assert.AreEqual(3,sim.ResetCount);
        }
        [Test] public void CancelledShotNeverRunsThePendingRefill()
        {
            var sim=ClearBoard();sim.Tick(2);sim.CancelShot();sim.Tick(6);
            Assert.AreEqual(1,sim.ResetCount);Assert.IsFalse(sim.AwaitingAllClearRefill);
            sim.ResetBoard();Assert.IsTrue(sim.Charges.All(c=>c.Active));
            Assert.IsTrue(sim.Launch(Vector2.up*300,spirit));sim.Tick(.1f);Assert.AreEqual(2,sim.ResetCount);
        }
        [Test] public void NaturallyStoppedShotDoesNotRefillAtTheFiveSecondBoundary()
        {
            tuning.maximumShotSeconds=2;
            var sim=ClearBoard();sim.Tick(6);
            Assert.IsFalse(sim.Moving);Assert.AreEqual(1,sim.ResetCount);Assert.IsFalse(sim.AwaitingAllClearRefill);
        }
        [Test] public void NoSimulationTimeLeavesThePendingTimerUnchanged()
        {
            var sim=ClearBoard();sim.Tick(2);float age=sim.AllClearWaitSeconds;
            sim.Tick(0);sim.Tick(float.NaN);sim.Tick(-1);
            Assert.AreEqual(age,sim.AllClearWaitSeconds);Assert.AreEqual(1,sim.ResetCount);
        }
        [Test] public void OneRemainingChargePreventsAnAllClear()
        {
            var sim=new ShotSimulation(tuning);
            foreach(var charge in sim.Charges)charge.Active=false;
            sim.Charges[0].Active=true;sim.Charges[0].Position=sim.Position+Vector2.up*5;
            sim.Charges[63].Active=true;
            Assert.IsTrue(sim.Launch(Vector2.up*300,spirit));sim.Tick(.001f);
            Assert.AreEqual(1,sim.Collected);Assert.AreEqual(0,sim.AllClearCount);Assert.IsFalse(sim.AwaitingAllClearRefill);
        }
    }
}
