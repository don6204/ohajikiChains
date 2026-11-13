using System;
using System.Collections.Generic;
using UnityEngine;
namespace Ohajiki
{
    public static class BattleRules
    {
        public static int EffectLevel(int count, BattleTuning tuning)
        {
            int level = 1; foreach (int threshold in tuning.effectThresholds) if (count >= threshold) level++;
            return level;
        }
        public static float Affinity(Element attack, Element target, BattleTuning tuning)
        {
            if (attack == target || attack == Element.Neutral || target == Element.Neutral) return MasterConstants.GetFloat("BattleRules.neutralAffinity",1);
            return (attack == Element.Fire && target == Element.Wood) || (attack == Element.Wood && target == Element.Water) ||
                (attack == Element.Water && target == Element.Fire) ? tuning.advantage : tuning.disadvantage;
        }
        public static int ContactDamage(int attack, int contacts, Element element, Element target, BattleTuning tuning) =>
            ContactDamage((float)attack, contacts, element, target, tuning);
        public static int ContactDamage(float attack, float contacts, Element element, Element target, BattleTuning tuning) =>
            Mathf.RoundToInt(attack * Mathf.Max(0, contacts) * tuning.contactAttackFactor * Affinity(element, target, tuning));
        public static int TotalHp(SpiritDefinition[] spirits)
        { int hp = 0; foreach (var spirit in spirits) hp += spirit.Hp; return hp; }
    }
    public sealed class SpiritDeck
    {
        public const int Size = 12, HandSize = 4;
        readonly List<int> draw = new List<int>(), discard = new List<int>();
        readonly System.Random random;
        readonly int[] hand = new int[HandSize];
        public int CardAt(int slot) => hand[slot];
        public int DrawCount => draw.Count;
        public SpiritDeck(int seed)
        {
            random = new System.Random(seed);
            for (int i = 0; i < Size; i++) draw.Add(i);
            Shuffle(); for (int i = 0; i < HandSize; i++) hand[i] = Take();
        }
        void Shuffle()
        {
            for (int i = draw.Count - 1; i > 0; i--) { int j = random.Next(i + 1); int tmp = draw[i]; draw[i] = draw[j]; draw[j] = tmp; }
        }
        int Take() { int index = draw.Count - 1, card = draw[index]; draw.RemoveAt(index); return card; }
        public void Replace(int slot)
        {
            discard.Add(hand[slot]);
            if (draw.Count == 0) { draw.AddRange(discard); discard.Clear(); Shuffle(); }
            hand[slot] = Take();
        }
    }
    public sealed class Combatant
    {
        public int MaxHp { get; }
        public int Hp { get; private set; }
        public Element Element { get; set; }
        public int GuardTurns { get; private set; }
        public float GuardValue { get; private set; }
        public bool PercentGuard { get; private set; }
        public float AttackMultiplier => attackBoost * attackReduction;
        public float SpeedMultiplier => speedBoost * speedReduction;
        public int AttackBoostTurns { get; private set; }
        public int SpeedBoostTurns { get; private set; }
        public int AttackReductionTurns { get; private set; }
        public int SpeedReductionTurns { get; private set; }
        float attackBoost = 1, speedBoost = 1, attackReduction = 1, speedReduction = 1;
        public void SetAttackBoost(float multiplier, int turns)
        { attackBoost = Mathf.Max(1, multiplier); AttackBoostTurns = Mathf.Max(1, turns); }
        public void SetSpeedBoost(float multiplier, int turns)
        { speedBoost = Mathf.Max(1, multiplier); SpeedBoostTurns = Mathf.Max(1, turns); }
        public void SetAttackReduction(float multiplier, int turns)
        { attackReduction = Mathf.Clamp(multiplier, MasterConstants.GetFloat("Combatant.minimumReductionMultiplier",.05f), 1); AttackReductionTurns = Mathf.Max(1, turns); }
        public void SetSpeedReduction(float multiplier, int turns)
        { speedReduction = Mathf.Clamp(multiplier, MasterConstants.GetFloat("Combatant.minimumReductionMultiplier",.05f), 1); SpeedReductionTurns = Mathf.Max(1, turns); }
        public void Cleanse()
        {
            attackReduction = speedReduction = 1;
            AttackReductionTurns = SpeedReductionTurns = 0;
        }
        internal void ConsumeStatusTurn()
        {
            if (AttackBoostTurns > 0 && --AttackBoostTurns == 0) attackBoost = 1;
            if (SpeedBoostTurns > 0 && --SpeedBoostTurns == 0) speedBoost = 1;
            if (AttackReductionTurns > 0 && --AttackReductionTurns == 0) attackReduction = 1;
            if (SpeedReductionTurns > 0 && --SpeedReductionTurns == 0) speedReduction = 1;
        }
        public int ReceiveTrap(int rawDamage) => ApplyDamage(MitigatedDamage(rawDamage));
        public Combatant(int hp, Element element) { MaxHp = Hp = hp; Element = element; }
        public int Heal(int amount) { int heal = Mathf.Min(Mathf.Max(0, amount), MaxHp - Hp); Hp += heal; return heal; }
        public void SetGuard(float value, int turns, bool percent) { GuardValue = Mathf.Max(0,value); GuardTurns = Mathf.Max(1,turns); PercentGuard = percent; }
        public int MitigatedDamage(int rawDamage)
        {
            float amount = Mathf.Max(0, rawDamage);
            if (GuardTurns > 0)
            {
                amount = PercentGuard ? amount * (1 - Mathf.Clamp01(GuardValue)) : Mathf.Max(0, amount - GuardValue);
            }
            return Mathf.RoundToInt(amount);
        }
        internal int ApplyDamage(int amount) {int damage=Mathf.Min(Hp,Mathf.Max(0,amount));Hp-=damage;return damage;}
        internal void ConsumeGuardTurn() {if(GuardTurns>0 && --GuardTurns==0)GuardValue=0;}
        public int Receive(int rawDamage) {int damage=ApplyDamage(MitigatedDamage(rawDamage));ConsumeGuardTurn();return damage;}
    }
    public struct ActionResult
    {
        public bool enemy;
        public int contacts, matched, level, normalDamage, skillDamage, appliedDamage, healing;
        public int normalAfterGuard,skillAfterGuard,appliedNormalDamage,appliedSkillDamage;
        public bool guarding;
        public string statusEffect;
        public string Summary => $"通常 {normalDamage} ＋ スキル {skillDamage} → {appliedDamage} ダメージ" + (healing > 0 ? $" / 回復 {healing}" : "");
    }
    public sealed class BattleState
    {
        public Combatant Player { get; }
        public Combatant Enemy { get; }
        public int Turn { get; private set; } = 1;
        public bool Practice { get; }
        public bool Finished => Player.Hp <= 0 || (!Practice && Enemy.Hp <= 0);
        public bool Won => Enemy.Hp <= 0 && Player.Hp>0;
        public bool EnemyActsNext { get; private set; }
        public bool ActionPending => pendingSpirit!=null;
        SpiritDefinition pendingSpirit;
        Combatant pendingSource,pendingTarget;
        ActionResult pendingResult;
        float pendingPower;
        public BattleState(BattleTuning tuning, bool practice=false)
        {
            Practice=practice;
            Player = new Combatant(BattleRules.TotalHp(tuning.playerDeck), tuning.playerDeck[0].element);
            Enemy = new Combatant(BattleRules.TotalHp(tuning.enemyDeck), tuning.enemyDeck[0].element);
        }
        public void ContinuePractice()
        {
            if(!Practice || Enemy.Hp>0 || ActionPending)throw new InvalidOperationException("Practice target is not defeated");
            EnemyActsNext=false;Turn++;
        }
        public ActionResult Resolve(bool enemy, SpiritDefinition spirit, int contacts, int matched, BattleTuning tuning)
        {
            ResolveNormal(enemy,spirit,contacts,matched,tuning);
            return ResolveSkill();
        }
        public ActionResult ResolveNormal(bool enemy, SpiritDefinition spirit, int contacts, int matched, BattleTuning tuning) =>
            ResolveNormal(enemy, spirit, contacts, matched, tuning, contacts);
        public ActionResult ResolveNormal(bool enemy, SpiritDefinition spirit, int contacts, int matched, BattleTuning tuning, float weightedContacts)
        {
            if (ActionPending || Finished || enemy != EnemyActsNext) throw new InvalidOperationException("Not this side's turn");
            if(spirit==null || !spirit.IsValid)throw new ArgumentException("Invalid acting spirit");
            var source = enemy ? Enemy : Player; var target = enemy ? Player : Enemy;
            source.Element = spirit.element;
            var result = new ActionResult { enemy = enemy, contacts = contacts, matched = matched, level = BattleRules.EffectLevel(matched, tuning) };
            result.normalDamage = BattleRules.ContactDamage(spirit.Attack * source.AttackMultiplier, weightedContacts, spirit.element, target.Element, tuning);
            float power = spirit.Power(result.level), affinity = BattleRules.Affinity(spirit.element, target.Element, tuning);
            switch (spirit.skill)
            {
                case Skill.Attack: result.skillDamage = Mathf.RoundToInt(power * affinity * source.AttackMultiplier); break;
                case Skill.PercentAttack: result.skillDamage = Mathf.RoundToInt(target.MaxHp * Mathf.Clamp01(power) * affinity * source.AttackMultiplier); break;
            }
            // Snapshot one action's mitigation. A fixed guard budget and percentage rounding
            // are shared by both stages, and its duration is consumed only after the skill.
            result.normalAfterGuard=target.MitigatedDamage(result.normalDamage);
            result.skillAfterGuard=target.MitigatedDamage(result.normalDamage+result.skillDamage)-result.normalAfterGuard;
            result.appliedNormalDamage=target.ApplyDamage(result.normalAfterGuard);
            result.appliedDamage=result.appliedNormalDamage;
            pendingSpirit=spirit;pendingSource=source;pendingTarget=target;pendingResult=result;pendingPower=power;
            return result;
        }
        public ActionResult ResolveSkill()
        {
            if(!ActionPending)throw new InvalidOperationException("Normal attack must resolve before the skill");
            var result=pendingResult;
            // Existing effects cover this action. Newly granted effects start with the next one.
            pendingSource.ConsumeStatusTurn();
            switch(pendingSpirit.skill)
            {
                case Skill.AttackUp:
                    pendingSource.SetAttackBoost(pendingPower,pendingSpirit.statusTurns);
                    result.statusEffect="攻撃力アップ";break;
                case Skill.SpeedUp:
                    pendingSource.SetSpeedBoost(pendingPower,pendingSpirit.statusTurns);
                    result.statusEffect="スピードアップ";break;
                case Skill.Cleanse:
                    pendingSource.Cleanse();result.statusEffect="悪効果解除";break;
                case Skill.AttackDown:
                    pendingTarget.SetAttackReduction(pendingPower,pendingSpirit.statusTurns);
                    result.statusEffect="敵の攻撃力ダウン";break;
                case Skill.SpeedDown:
                    pendingTarget.SetSpeedReduction(pendingPower,pendingSpirit.statusTurns);
                    result.statusEffect="敵のスピードダウン";break;
                case Skill.Heal: result.healing=pendingSource.Heal(Mathf.RoundToInt(pendingPower));break;
                case Skill.PercentHeal: result.healing=pendingSource.Heal(Mathf.RoundToInt(pendingSource.MaxHp*Mathf.Clamp01(pendingPower)));break;
                case Skill.Guard: pendingSource.SetGuard(pendingPower,pendingSpirit.guardTurns,false);result.guarding=true;break;
                case Skill.PercentGuard: pendingSource.SetGuard(pendingPower,pendingSpirit.guardTurns,true);result.guarding=true;break;
            }
            result.appliedSkillDamage=pendingTarget.ApplyDamage(result.skillAfterGuard);
            result.appliedDamage+=result.appliedSkillDamage;pendingTarget.ConsumeGuardTurn();
            EnemyActsNext=!result.enemy;if(result.enemy)Turn++;
            pendingSpirit=null;pendingSource=pendingTarget=null;
            return result;
        }
    }
}
