using System;
using UnityEngine;
namespace Ohajiki
{
    [Serializable] public struct RankBand
    {
        public int throughRank, baseExperience, perCurrentRank;
        public RankBand(int max, int basis, int perRank) { throughRank = max; baseExperience = basis; perCurrentRank = perRank; }
    }
    [CreateAssetMenu(menuName = "Ohajiki/Battle Tuning")]
    public sealed class BattleTuning : ScriptableObject
    {
        [Header("編成 / 各12体 / 無属性スピリターは禁止")]
        public SpiritDefinition[] playerDeck = new SpiritDefinition[12];
        public SpiritDefinition[] enemyDeck = new SpiritDefinition[12];
        [Header("仕様書に記載の数値")]
        public int[] effectThresholds = { 1, 3, 5, 10 };
        [Min(0)] public float contactAttackFactor = .15f;
        [Min(0)] public float advantage = 1.2f;
        [Min(0)] public float disadvantage = .8f;
        [Header("ショット / 速度は各キャラのデータで設定")]
        [Min(1)] public float deceleration = 200;
        [Range(10, 30)] public float shotRadius = 20;
        [Range(20, 30)] public float chargeRadius = 22;
        [Tooltip("見た目は円、当たり判定は軸に平行な四角形。各Radiusは半辺の長さ")]
        public bool squareHitboxes=true;
        [Min(.1f)] public float maximumShotSeconds = 8;
        [Min(1)] public float stopSpeed = 35;
        [Min(1)] public float minimumSwipe = 25;
        [Min(1)] public float minimumForwardSwipe = 15;
        [Min(0)] public float touchPadding = 22;
        public int seed = 43;
        [Header("特殊チャージ / 攻撃・速度・分裂・縦・横・ワープの順")]
        [Tooltip("各枠の出現確率。通常チャージは残りの確率。合計1以下")]
        public float[] specialChargeProbabilities = { .05f, .05f, .05f, .05f, .05f, .10f };
        [Min(1)] public float specialAttackMultiplier = 1.5f;
        [Min(1)] public float specialSpeedMultiplier = 2.5f;
        [Min(2)] public int specialSplitTotal = 3;
        [Header("特殊効果の上限 / 仮の調整値")]
        [Range(3, 32)] public int maximumProjectiles = 12;
        [Min(1)] public float maximumShotSpeed = 6000;
        [Min(1)] public float maximumContactMultiplier = 8;
        [Range(1, 24)] public int maximumTraps = 12;
        [Min(1)] public float trapRadius = 24;
        [Header("盤面・AI / 位置は盤面幅高さに対する割合")]
        [Range(3, 8)] public int boardColumns = 8;
        [Range(3, 8)] public int boardRows = 8;
        [Range(.1f, .4f)] public float boardBottom = .22f;
        [Range(.6f, .85f)] public float boardTop = .78f;
        [Header("全消し / ゲーム内時間")]
        [Min(.1f)] public float allClearRefillDelay = 5;
        [Range(1, 2)] public float allClearBannerSeconds = 1.5f;
        public Vector4 elementWeights = Vector4.one;
        [Range(.04f, .2f)] public float launchInset = .1f;
        [Range(.05f, .35f)] public float enemyMinimumAngle = .18f;
        [Min(0)] public float enemyThinkSeconds = .8f;
        [Header("演出・入力 / 秒")]
        [Min(.05f)] public float chargeSeconds = .7f;
        [Min(.05f)] public float cutInSeconds = .7f;
        [Min(.05f)] public float impactSeconds = .7f;
        [Min(.05f)] public float normalImpactSeconds = .65f;
        [Min(.05f)] public float damageFlightSeconds=.48f;
        [Header("接触フィードバック / ダメージ計算への影響なし")]
        [Range(0,2)] public float contactEffectIntensity=1;
        [Range(.15f,1)] public float contactEffectSeconds=.48f;
        [Range(4,32)] public int contactEffectPoolSize=16;
        [HideInInspector] public float damageNumberPunch;
        [Min(1)] public float damageCounterSpeed=14;
        [Tooltip("ダメージ加算UIの数字が黄→オレンジ→赤になる表示上の基準。計算には影響しません")]
        [Min(1)] public int damageColorFullAt=600;
        [Range(0,1)] public float trailOpacity=.38f;
        [HideInInspector] public float damageBankShake;
        [HideInInspector] public float damageBankTilt;
        [HideInInspector] public int presentationVersion;
        public int[] contactCelebrationThresholds={5,10,20};
        [Min(.1f)] public float longPressSeconds = .55f;
        [Min(1)] public float effectMaxDiameter = 160;
        [HideInInspector] public float hitShake;
        [Min(1)] public float levelSlideSpeed = 950;
        [Header("報酬・ランク / 現時点はセッション内のみ")]
        [Min(0)] public int rewardExperience = 50;
        [Min(0)] public int rewardCoins = 500;
        public bool rewardOnDefeat = true;
        [Min(2)] public int maxRank = 50;
        [Min(1)] public int rankTwoExperience = 100;
        public RankBand[] rankBands = { new RankBand(10,450,50), new RankBand(20,600,60), new RankBand(30,800,75), new RankBand(50,1000,100) };
        public bool Validate(out string error)
        {
            foreach (var deck in new[] { playerDeck, enemyDeck })
            {
                if (deck == null || deck.Length != SpiritDeck.Size) { error = "デッキは12体必要です"; return false; }
                foreach (var spirit in deck)
                    if (spirit == null || !spirit.IsValid) { error = "キャラデータが未設定、または無属性です"; return false; }
            }
            if (effectThresholds == null || effectThresholds.Length == 0) { error = "効果Lvの閾値が必要です"; return false; }
            for (int i = 0; i < effectThresholds.Length; i++)
                if (effectThresholds[i] < 1 || (i > 0 && effectThresholds[i] <= effectThresholds[i-1])) { error = "効果Lvの閾値は昇順で指定してください"; return false; }
            if (elementWeights.x < 0 || elementWeights.y < 0 || elementWeights.z < 0 || elementWeights.w < 0 || elementWeights.x + elementWeights.y + elementWeights.z + elementWeights.w <= 0)
            { error = "チャージボールの属性比率が不正です"; return false; }
            if (rankBands == null || rankBands.Length == 0 || rankBands[rankBands.Length-1].throughRank < maxRank)
            { error = "最大ランクまでの経験値テーブルが必要です"; return false; }
            if (specialChargeProbabilities == null || specialChargeProbabilities.Length != 6)
            { error = "特殊チャージ6種類の確率が必要です"; return false; }
            float specialTotal = 0;
            foreach (float chance in specialChargeProbabilities)
            {
                if (float.IsNaN(chance) || float.IsInfinity(chance) || chance < 0 || chance > 1)
                { error = "特殊チャージの確率は0〜1で指定してください"; return false; }
                specialTotal += chance;
            }
            if (specialTotal > 1 || maximumProjectiles < 3 || maximumTraps < 1 || maximumShotSpeed <= 0 ||
                specialAttackMultiplier < 1 || specialSpeedMultiplier < 1 || specialSplitTotal < 2 ||
                maximumContactMultiplier < 1 || trapRadius <= 0 || boardColumns < 2 || boardRows < 2 ||
                shotRadius <= 0 || maximumShotSeconds <= 0)
            { error = "特殊効果または盤面の調整値が不正です"; return false; }
            if (float.IsNaN(allClearRefillDelay) || float.IsInfinity(allClearRefillDelay) || allClearRefillDelay < .1f ||
                float.IsNaN(allClearBannerSeconds) || allClearBannerSeconds < 1 || allClearBannerSeconds > 2)
            { error = "全消しの再配置待機時間、または表示時間が不正です"; return false; }
            error = null; return true;
        }
    }
}
