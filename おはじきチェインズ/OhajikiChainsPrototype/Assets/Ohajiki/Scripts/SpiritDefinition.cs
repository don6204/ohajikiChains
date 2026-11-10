using UnityEngine;
namespace Ohajiki
{
    public enum Element { Fire, Water, Wood, Neutral }
    public enum Skill { Attack, Heal, Guard, PercentAttack, PercentHeal, PercentGuard }
    public enum BattlePhase { Ready, PlayerShot, Charging, CutIn, Impact, EnemyThinking, EnemyShot, Finished, NormalImpact, DamageFlight }

    [CreateAssetMenu(menuName = "Ohajiki/Spirit Definition")]
    public sealed class SpiritDefinition : ScriptableObject
    {
        public string displayName;
        public Sprite portrait;
        public Element element;
        [Range(1, 5)] public int rarity = 1;
        [Min(1)] public int level = 1;
        [Min(1)] public int baseHp = 100;
        [Min(0)] public int hpPerLevel = 10;
        [Min(0)] public int baseAttack = 220;
        [Min(0)] public int attackPerLevel = 12;
        [Min(1)] public float baseSpeed = 1250;
        [Min(0)] public float speedPerLevel = 8;
        public string skillName = "スピリットバースト";
        public Skill skill;
        [Tooltip("効果Lvごとの値。割合系は0〜1。具体的な数値は仮データ")]
        public float[] skillPower = { 100, 145, 190, 235, 280 };
        [Min(1)] public int guardTurns = 2;
        public int Hp => baseHp + hpPerLevel * (level - 1);
        public int Attack => baseAttack + attackPerLevel * (level - 1);
        public float Speed => baseSpeed + speedPerLevel * (level - 1);
        public float Power(int effectLevel) => skillPower[Mathf.Clamp(effectLevel - 1, 0, skillPower.Length - 1)];
        public bool IsValid => element >= Element.Fire && element <= Element.Wood && rarity>=1 && rarity<=5 && level > 0 && baseHp > 0 && baseSpeed > 0 && skillPower != null && skillPower.Length > 0;
    }
}
