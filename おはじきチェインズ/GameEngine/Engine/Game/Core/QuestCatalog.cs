#nullable disable
// Ported from the Unity game. Rendering and engine primitives are provided by DirectX 12 / PortableTypes.
using System;
using System.Collections.Generic;
using System.Linq;
using Ohajiki.Portable;

namespace Ohajiki
{
    public enum QuestCategory { Main, Event, Showdown, Materials, Challenge, Practice }
    public enum QuestDifficulty { Standard, Easy, Normal, Hard }

    [Serializable]
    public sealed class QuestRewardEntry
    {
        public string id;
        public int spiritIndex=-1;
        public ShopRewardKind itemKind;
        [Range(0,1)] public float probability=1;
        [Min(1)] public int min=1,max=1;
    }

    [Serializable]
    public sealed class QuestDefinition
    {
        public string id, groupId, title;
        [TextArea] public string description;
        public Sprite banner,enemyPortrait;
        public bool useEnemyPortrait;
        public QuestCategory category;
        public QuestDifficulty difficulty;
        [Min(1)] public int order = 1;
        public string prerequisiteId;
        [Tooltip("日本時間 yyyy-MM-dd。空欄は開催中。終了日は含まない")]
        public string availableFrom, availableUntil;
        public bool enabled = true;
        [Min(0)] public int energyCost = 1;
        [Header("仮の敵編成 / 空欄なら共通の敵から属性に合わせて編成")]
        public SpiritDefinition[] enemyDeck;
        public bool preserveEnemyLevels;
        public Element enemyElement;
        [Range(1,60)] public int enemyLevel = 1;
        [Min(.01f)] public float hpMultiplier = 1, attackMultiplier = 1, speedMultiplier = 1;
        [Header("クリアごとの報酬 / 敗北・通常リタイア時はなし")]
        [Min(0)] public int experience = 50, medals = 500;
        [Min(0)] public int gems, shiningStones, eventPoints, materialCount;
        [Range(0,3)] public int materialType = 3;
        [Header("スピリター報酬 / カタログ番号、-1はなし")]
        public int rewardSpiritIndex = -1;
        [Range(0,1)] public float spiritDropChance;
        public bool firstClearSpirit;
        [Header("初回クリアのみ")]
        [Min(0)] public int firstClearGems;
        // Explicit flag survives Unity's null-array-to-empty serialization.
        public bool useMasterRewards;
        public QuestRewardEntry[] masterRewards;
        public string DifficultyLabel => difficulty == QuestDifficulty.Easy ? "易" :
            difficulty == QuestDifficulty.Normal ? "普通" : difficulty == QuestDifficulty.Hard ? "難" : "";
        public string DisplayTitle => title + (difficulty == QuestDifficulty.Standard ? "" : " / " + DifficultyLabel);
        public bool IsPractice => category == QuestCategory.Practice;
        public bool CalendarOpen(DateTime today) => enabled && ShopCalendar.IsOpen(availableFrom, availableUntil, today);
        public ShopReward[] Rewards(bool first) => new[] {
            new ShopReward { kind=ShopRewardKind.Gems, amount=gems+(first?firstClearGems:0) },
            new ShopReward { kind=ShopRewardKind.ShiningStones, amount=shiningStones },
            new ShopReward { kind=ShopRewardKind.EventPoints, amount=eventPoints },
            new ShopReward { kind=(ShopRewardKind)((int)ShopRewardKind.FirePower+materialType), amount=materialCount }
        }.Where(r=>r.amount>0).ToArray();
    }

    [CreateAssetMenu(menuName="Ohajiki/Quest Catalog")]
    public sealed class QuestCatalog : ScriptableObject
    {
        public QuestDefinition[] quests = Array.Empty<QuestDefinition>();
        public QuestDefinition Find(string id) => quests?.FirstOrDefault(q=>q!=null && q.id==id);
        public bool Validate(int spiritCount, out string error)
        {
            error=null;
            if(quests==null || quests.Length==0){error="クエスト一覧が空です";return false;}
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var q in quests)
            {
                if(q==null || string.IsNullOrWhiteSpace(q.id) || !ids.Add(q.id) || string.IsNullOrWhiteSpace(q.title))
                {error="クエストID・名前が空欄、またはIDが重複しています";return false;}
                if(!Enum.IsDefined(typeof(QuestCategory),q.category) || !Enum.IsDefined(typeof(QuestDifficulty),q.difficulty) ||
                    q.enemyElement<Element.Fire || q.enemyElement>Element.Wood || q.enemyLevel<1 || q.enemyLevel>60 ||
                    !Positive(q.hpMultiplier) || !Positive(q.attackMultiplier) || !Positive(q.speedMultiplier) ||
                    q.energyCost<0 || q.energyCost>1000 || q.order<1 || q.experience<0 || q.medals<0 || q.gems<0 ||
                    q.firstClearGems<0 || (long)q.gems+q.firstClearGems>int.MaxValue || q.shiningStones<0 || q.eventPoints<0 ||
                    q.materialCount<0 || q.materialType<0 || q.materialType>3 || q.rewardSpiritIndex < -1 || q.rewardSpiritIndex>=spiritCount ||
                    float.IsNaN(q.spiritDropChance) || q.spiritDropChance<0 || q.spiritDropChance>1 ||
                    (q.rewardSpiritIndex<0 && (q.spiritDropChance>0 || (q.firstClearSpirit && !q.useMasterRewards))))
                {error=q.id+": 数値・属性・報酬の設定が不正です";return false;}
                if(q.enemyDeck!=null && q.enemyDeck.Length>0 &&
                    (q.enemyDeck.Length!=SpiritDeck.Size || q.enemyDeck.Any(s=>s==null || !s.IsValid)))
                {error=q.id+": 敵デッキには有効な12体が必要です";return false;}
                if(q.preserveEnemyLevels && (q.enemyDeck==null || q.enemyDeck.Length!=SpiritDeck.Size))
                {error=q.id+": 個別レベルには敵デッキが必要です";return false;}
                if(q.useMasterRewards)
                {
                    if(q.masterRewards==null){error=q.id+": マスター報酬配列が未設定です";return false;}
                    var rewardIds=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var reward in q.masterRewards)
                        if(reward==null || string.IsNullOrWhiteSpace(reward.id) || !rewardIds.Add(reward.id) ||
                            reward.spiritIndex < -1 || reward.spiritIndex>=spiritCount ||
                            !Enum.IsDefined(typeof(ShopRewardKind),reward.itemKind) ||
                            float.IsNaN(reward.probability) || float.IsInfinity(reward.probability) ||
                            reward.probability<0 || reward.probability>1 || reward.min<1 || reward.max<reward.min)
                        {error=q.id+": マスター報酬のID・参照・数量・確率が不正です";return false;}
                    if(q.firstClearSpirit && !q.masterRewards.Any(r=>r.spiritIndex>=0 &&
                        (q.rewardSpiritIndex<0 || r.spiritIndex==q.rewardSpiritIndex)))
                    {error=q.id+": 初回確定スピリターの報酬行がありません";return false;}
                }
                if(!ValidDate(q.availableFrom) || !ValidDate(q.availableUntil) ||
                    (!string.IsNullOrEmpty(q.availableFrom) && !string.IsNullOrEmpty(q.availableUntil) &&
                     string.CompareOrdinal(q.availableFrom,q.availableUntil)>=0))
                {error=q.id+": 開催期間が不正です";return false;}
            }
            foreach(var q in quests)
            {
                var visited=new HashSet<string>{q.id};var cursor=q;
                while(!string.IsNullOrEmpty(cursor.prerequisiteId))
                {
                    cursor=Find(cursor.prerequisiteId);
                    if(cursor==null || !visited.Add(cursor.id)){error=q.id+": 解放条件が存在しないか循環しています";return false;}
                }
            }
            return true;
        }
        static bool Positive(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x)&&x>0&&x<=100;
        static bool ValidDate(string value)=>string.IsNullOrEmpty(value)||DateTime.TryParseExact(value,"yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _);
        public static string CategoryName(QuestCategory category)
        {
            string[] names={"メインクエスト","イベントクエスト","決戦イベント","強化・素材クエスト","挑戦クエスト","練習クエスト"};
            return names[(int)category];
        }
    }
}
