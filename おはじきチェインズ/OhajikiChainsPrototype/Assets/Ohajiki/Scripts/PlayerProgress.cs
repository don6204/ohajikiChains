using UnityEngine;
namespace Ohajiki
{
    // Session-only until save and account specifications are supplied.
    public sealed class PlayerProgress
    {
        // PrototypeBattle owns one of these as a field initializer; defer resource access until use.
        int rank;
        public int Rank
        {
            get {if(rank==0)rank=MasterConstants.GetInt("PlayerProgress.initialRank",1);return rank;}
            private set {rank=value;}
        }
        public long TotalExperience { get; private set; }
        public long RankExperience { get; private set; }
        public long Coins { get; private set; }
        internal void SetShopCoins(long value)
        {
            if(value<0)throw new System.ArgumentOutOfRangeException(nameof(value));
            Coins=value;
        }
        public bool TrySpendCoins(long amount)
        {
            if (amount < 0 || Coins < amount) return false;
            Coins -= amount; return true;
        }
        public int Needed(BattleTuning tuning)
        {
            if (Rank >= tuning.maxRank) return 0;
            if (Rank == 1) return tuning.rankTwoExperience;
            foreach (var band in tuning.rankBands)
                if (Rank + 1 <= band.throughRank) return Mathf.Max(1, band.baseExperience + Rank * band.perCurrentRank);
            throw new System.InvalidOperationException("Rank table does not cover maximum rank");
        }
        public void Award(int experience, int coins, BattleTuning tuning)
        {
            TotalExperience += Mathf.Max(0, experience); RankExperience += Mathf.Max(0, experience); Coins += Mathf.Max(0, coins);
            while (Rank < tuning.maxRank)
            { int required = Needed(tuning); if (RankExperience < required) break; RankExperience -= required; Rank++; }
        }
    }
}
