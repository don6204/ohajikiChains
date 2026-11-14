#nullable disable
// Ported from the Unity game. Rendering and engine primitives are provided by DirectX 12 / PortableTypes.
using Ohajiki.Portable;
namespace Ohajiki
{
    public enum Element { Fire, Water, Wood, Neutral }
    public enum Skill { Attack, Heal, Guard, PercentAttack, PercentHeal, PercentGuard, AttackUp, SpeedUp, Cleanse, AttackDown, SpeedDown }
    public enum ActionSkill { None, Trap, Split }
    public enum BattlePhase { Ready, PlayerShot, Charging, CutIn, Impact, EnemyThinking, EnemyShot, Finished, NormalImpact, DamageFlight }

    [CreateAssetMenu(menuName = "Ohajiki/Spirit Definition")]
    public sealed class SpiritDefinition : ScriptableObject
    {
        public string masterId;
        public string displayName;
        [TextArea] public string description;
        public Sprite portrait;
        public Element element;
        [Range(1, 5)] public int rarity = 1;
        [Min(1)] public int level = 1;
        [Range(1,60)] public int maxLevel = 60;
        [Min(0)] public int experienceBase;
        [Min(0)] public int experiencePerLevel;
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
        [Min(1)] public int statusTurns = 2;
        [Header("追加アクションスキル / 未確定値は仮設定")]
        public ActionSkill actionSkill;
        public Element trapTargetElement = Element.Fire;
        [Min(0)] public int trapDamage = 100;
        [Min(1)] public int trapOpponentTurns = 2;
        [Min(1)] public int splitContactThreshold = 3;
        [Range(2, 12)] public int splitTotalProjectiles = 3;
        public int Hp => baseHp + hpPerLevel * (level - 1);
        public int Attack => baseAttack + attackPerLevel * (level - 1);
        public float Speed => baseSpeed + speedPerLevel * (level - 1);
        public float Power(int effectLevel) => skill == Skill.Cleanse ? 0 : skillPower[Mathf.Clamp(effectLevel - 1, 0, skillPower.Length - 1)];
        public bool IsValid
        {
            get
            {
                if (element < Element.Fire || element > Element.Wood || rarity < 1 || rarity > 5 ||
                    level < 1 || level > maxLevel || maxLevel < 1 || maxLevel > 60 || experienceBase < 0 || experiencePerLevel < 0 ||
                    baseHp <= 0 || baseSpeed <= 0 || float.IsNaN(baseSpeed) ||
                    float.IsInfinity(baseSpeed) || skill < Skill.Attack || skill > Skill.SpeedDown ||
                    (skill != Skill.Cleanse && (skillPower == null || skillPower.Length == 0)) || guardTurns < 1 || statusTurns < 1)
                    return false;
                foreach (float power in skillPower ?? System.Array.Empty<float>())
                    if (power < 0 || float.IsNaN(power) || float.IsInfinity(power)) return false;
                if (actionSkill < ActionSkill.None || actionSkill > ActionSkill.Split) return false;
                if (actionSkill == ActionSkill.Trap && (trapTargetElement < Element.Fire ||
                    trapTargetElement > Element.Wood || trapDamage < 0 || trapOpponentTurns < 1)) return false;
                if (actionSkill == ActionSkill.Split && (splitContactThreshold < 1 ||
                    splitTotalProjectiles < 2 || splitTotalProjectiles > 12)) return false;
                return true;
            }
        }
    }
}
