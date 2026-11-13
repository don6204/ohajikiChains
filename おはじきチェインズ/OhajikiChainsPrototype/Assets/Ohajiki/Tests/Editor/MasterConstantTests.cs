using NUnit.Framework;

namespace Ohajiki.Tests
{
    public sealed class MasterConstantTests
    {
        static MasterConstants.Entry Entry(string id,double value)=>new MasterConstants.Entry{id=id,value=value};
        [TestCase("SpiritDeck.Size",13)]
        [TestCase("SpiritDeck.HandSize",5)]
        [TestCase("ShotSimulation.Width",720)]
        [TestCase("ShotSimulation.Height",1280)]
        [TestCase("ShopState.premiumDailyClaimLimit",2)]
        public void UnsupportedStructuralChangesAreRejectedRatherThanIgnored(string id,double value)
        {
            Assert.IsTrue(MasterConstants.Supports(id));
            Assert.IsFalse(MasterConstants.ValidateEntries(new[]{Entry(id,value)},out string error));
            StringAssert.Contains(id,error);
        }
        [TestCase("TitleView.startFadeSeconds",0)]
        [TestCase("GachaSequence.releaseFlashAlpha",1.1)]
        [TestCase("ShopCalendar.utcOffsetHours",15)]
        [TestCase("GachaStage3D.moteCount",1.5)]
        public void RuntimeUnsafeValuesAreRejected(string id,double value)
        {
            Assert.IsFalse(MasterConstants.ValidateEntries(new[]{Entry(id,value)},out _));
        }
        [Test] public void RejectsNonfiniteDuplicateAndUnknownConstants()
        {
            const string id="TitleView.startFadeSeconds";
            Assert.IsFalse(MasterConstants.ValidateEntries(new[]{Entry(id,double.NaN)},out _));
            Assert.IsFalse(MasterConstants.ValidateEntries(new[]{Entry(id,double.PositiveInfinity)},out _));
            Assert.IsFalse(MasterConstants.ValidateEntries(new[]{Entry(id,.5),Entry(id,.6)},out _));
            Assert.IsFalse(MasterConstants.ValidateEntries(new[]{Entry("TitleView.missing",1)},out _));
        }
        [Test] public void ThresholdOrderingAlsoUsesDefaultsForOmittedEntries()
        {
            Assert.IsFalse(MasterConstants.ValidateEntries(new[]{Entry("BattleFeedback.bankScaleThreshold2",400)},out _));
            Assert.IsTrue(MasterConstants.ValidateEntries(new[]{Entry("BattleFeedback.bankScaleThreshold1",100),Entry("BattleFeedback.bankScaleThreshold2",400)},out string error),error);
        }
        [Test] public void AllowsZeroDecorativeEffectsAndFractionalDurationWithoutChangingEntries()
        {
            var entries=new[]{Entry("GachaSequence.releaseRayCount",0),Entry("GachaStage3D.moteCount",0),Entry("TitleView.startFadeSeconds",.25)};
            Assert.IsTrue(MasterConstants.ValidateEntries(entries,out string error),error);
            Assert.AreEqual(.25,entries[2].value);
        }
        [TestCase(1.1f)] [TestCase(1.7f)]
        public void ApprovedRawReductionAboveOneKeepsStatsUnchanged(float power)
        {
            var actor=new Combatant(100,Element.Fire);
            actor.SetAttackReduction(power,2);actor.SetSpeedReduction(power,2);
            Assert.AreEqual(1,actor.AttackMultiplier);
            Assert.AreEqual(1,actor.SpeedMultiplier);
            Assert.AreEqual(2,actor.AttackReductionTurns);
            Assert.AreEqual(2,actor.SpeedReductionTurns);
        }
    }
}
