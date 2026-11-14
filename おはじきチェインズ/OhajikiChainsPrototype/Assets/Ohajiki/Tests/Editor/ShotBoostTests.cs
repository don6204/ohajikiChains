using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class ShotBoostTests
    {
        [TestCase(false)] [TestCase(true)]
        public void BoostsAffectThisShotImmediatelyWithoutMultiplyingHitCountAndResetNextShot(bool enemy)
        {
            var tuning=ScriptableObject.CreateInstance<BattleTuning>();
            var spirit=ScriptableObject.CreateInstance<SpiritDefinition>();
            try
            {
                tuning.deceleration=0;tuning.maximumShotSeconds=30;spirit.baseSpeed=600;
                var sim=new ShotSimulation(tuning);
                foreach(var charge in sim.Charges)charge.Active=false;
                sim.Prepare(enemy,312);
                Assert.IsTrue(sim.Launch((enemy?Vector2.down:Vector2.up)*300,spirit));
                int slot=0;
                void Hit(SpecialCharge special)
                {
                    var c=sim.Charges[slot++];c.Active=true;c.Special=special;c.Element=spirit.element;
                    c.Position=sim.Position+sim.Velocity.normalized*5;
                    sim.Tick(.0001f);
                }
                Hit(SpecialCharge.AttackBoost);
                Assert.AreEqual(1,sim.Collected);
                Assert.AreEqual(tuning.specialAttackMultiplier,sim.DamageWeight,1e-6);
                Hit(SpecialCharge.None);
                Assert.AreEqual(2,sim.Collected);
                Assert.AreEqual(2*tuning.specialAttackMultiplier,sim.WeightedContacts,.0001f);
                Assert.AreEqual(2.05*tuning.specialAttackMultiplier,sim.DamageWeight,1e-6);
                float speed=sim.Velocity.magnitude;
                Hit(SpecialCharge.SpeedBoost);
                Assert.AreEqual(3,sim.Collected);
                Assert.AreEqual(speed*tuning.specialSpeedMultiplier,sim.Velocity.magnitude,.01f);
                Hit(SpecialCharge.None);
                Assert.AreEqual(4,sim.Collected);
                Assert.AreEqual(4*tuning.specialAttackMultiplier,sim.WeightedContacts,.0001f);
                Assert.AreEqual(4.30*tuning.specialAttackMultiplier,sim.DamageWeight,1e-6);
                Assert.AreEqual(speed*tuning.specialSpeedMultiplier,sim.Velocity.magnitude,.01f);
                sim.CancelShot();
                sim.Prepare(enemy,312);
                Assert.IsTrue(sim.Launch((enemy?Vector2.down:Vector2.up)*300,spirit));
                Assert.AreEqual(spirit.Speed,sim.Velocity.magnitude,.01f);
                Assert.AreEqual(0,sim.Collected);Assert.AreEqual(0,sim.WeightedContacts);
                Assert.AreEqual(0,sim.DamageWeight);
                Hit(SpecialCharge.None);
                Assert.AreEqual(1,sim.Collected);Assert.AreEqual(1,sim.WeightedContacts);
                Assert.AreEqual(1,sim.DamageWeight);
            }
            finally{Object.DestroyImmediate(spirit);Object.DestroyImmediate(tuning);}
        }
    }
}
