using System;
using UnityEngine;

namespace Ohajiki
{
    public enum GachaKind { Standard, Event, Limited, Paid }
    [Serializable]
    public sealed class GachaEntry
    {
        public int spiritIndex;
        [Min(0)] public int weight = 1;
    }
    [Serializable]
    public sealed class GachaBanner
    {
        public string title = "スピリターの集い";
        [TextArea] public string description = "スピリターたちが集まる召喚。";
        public int featuredIndex;
        [Tooltip("目玉が複数いる場合のカタログ番号。空欄ならFeatured Indexのみ")]
        public int[] featuredIndices;
        public GachaEntry[] pool;
        public GachaKind kind;
        [Tooltip("★1〜★5の順。未設定の場合のみ従来のキャラ重み抽選")]
        public int[] rarityWeights;
        [Tooltip("日本時間 yyyy-MM-dd。空欄は試作開放。終了日は含まない")]
        public string availableFrom,availableUntil;
    }

    [CreateAssetMenu(menuName = "Ohajiki/Home Config")]
    public sealed class HubConfig : ScriptableObject
    {
        [Header("試作用の所持キャラ / 本番の所持データとは別")]
        public SpiritDefinition[] catalog;
        [Min(12)] public int startingOwnedCount = 12;
        [Range(1, 5)] public int deckSlots = 3;
        [Header("仮の初期資源 / アプリ終了でリセット")]
        [Min(0)] public int startingCoins = 5000;
        [Min(0)] public int startingGems = 0;
        [Min(1)] public int maximumEnergy = 5;
        [Min(0)] public int battleEnergyCost = 1;
        [Min(0)] public int startingMaterials = 20;
        [Header("仮の強化値 / 1素材で1レベル、コスト = 現在Lv × 単価")]
        [Range(1, 60)] public int maxSpiritLevel = 60;
        [Min(0)] public int upgradeCoinUnit = 50;
        [Header("バトル経験値 / 出撃した各スピリターへ付与する仮設定")]
        [Min(1)] public int spiritExperienceBase = 100;
        [Min(0)] public int spiritExperiencePerLevel = 25;
        [Min(0)] public float battleSpiritExperienceMultiplier = 1;
        public GachaBanner[] banners;
        [Header("ガチャ / 初回1連は各バナーで1回")]
        [Min(0)] public int firstSingleGems=1;
        [Min(0)] public int singleGems=5;
        [Min(0)] public int tenGems=50;
        [Min(0)] public int tenBonusGems=1;
        [Min(.5f)] public float featuredCycleSeconds=5;
        [HideInInspector] public int gachaPresentationVersion;
        public ShopConfig shop;
        [HideInInspector] public int specificationVersion;

        public bool Validate(out string error)
        {
            if (catalog == null || catalog.Length < SpiritDeck.Size) { error = "所持キャラは12体以上必要です"; return false; }
            foreach (var spirit in catalog)
                if (spirit == null || !spirit.IsValid) { error = "所持キャラの設定に不備があります"; return false; }
            if (startingOwnedCount < SpiritDeck.Size || startingOwnedCount > catalog.Length)
            { error = "初期所持数は12体以上、カタログ数以下が必要です"; return false; }
            if (deckSlots < 1 || maximumEnergy < 1 || battleEnergyCost < 0 || battleEnergyCost > maximumEnergy ||
                maxSpiritLevel < 1 || maxSpiritLevel > 60 || spiritExperienceBase < 1 || spiritExperiencePerLevel < 0 ||
                battleSpiritExperienceMultiplier < 0 || float.IsNaN(battleSpiritExperienceMultiplier) || float.IsInfinity(battleSpiritExperienceMultiplier) || upgradeCoinUnit < 0 || startingCoins < 0 || startingMaterials < 0 || startingGems<0 ||
                firstSingleGems<0 || singleGems<0 || tenGems<0 || tenBonusGems<0 || featuredCycleSeconds<.5f)
            { error = "ホームの初期値・強化値が不正です"; return false; }
            if (banners == null) { error = "ガチャバナーの一覧が未設定です"; return false; }
            foreach (var banner in banners)
            {
                if (banner == null || banner.featuredIndex < 0 || banner.featuredIndex >= catalog.Length)
                { error = "ガチャバナーのキャラ参照が不正です"; return false; }
                if(banner.featuredIndices!=null)foreach(int id in banner.featuredIndices)
                    if(id<0 || id>=catalog.Length){error="目玉キャラ一覧の参照が不正です";return false;}
                if (banner.pool == null || banner.pool.Length == 0) { error = "抽選候補が必要です"; return false; }
                long weight=0;
                foreach (var entry in banner.pool)
                {
                    if(entry==null || entry.spiritIndex<0 || entry.spiritIndex>=catalog.Length || entry.weight<0)
                    { error="抽選候補の設定が不正です";return false; }
                    weight+=entry.weight;
                }
                if(weight<=0 || weight>int.MaxValue) { error="抽選の合計重みが不正です";return false; }
            }
            foreach(var banner in banners)
                if(!GachaRules.Validate(this,banner,out error))return false;
            if(shop!=null && !shop.Validate(out error))return false;
            error = null; return true;
        }
    }
}
