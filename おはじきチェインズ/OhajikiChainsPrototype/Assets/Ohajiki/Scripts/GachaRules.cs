using System;
using System.Linq;

namespace Ohajiki
{
    public static class GachaRules
    {
        public static bool UsesRarity(GachaBanner banner) => banner.rarityWeights!=null && banner.rarityWeights.Length>0;
        public static bool Validate(HubConfig config,GachaBanner banner,out string error)
        {
            error=null;
            if(!ShopCalendar.ValidWindow(banner.availableFrom,banner.availableUntil)) {error="ガチャ期間が不正です";return false;}
            if(!UsesRarity(banner))return true;
            var weights=banner.rarityWeights;
            if(weights.Length!=5 || weights.Any(w=>w<0) || weights.Sum(w=>(long)w)!=100)
            {error="★1〜★5の排出率の合計を100にしてください";return false;}
            for(int rarity=1;rarity<=5;rarity++)
                if(weights[rarity-1]>0 && !banner.pool.Any(e=>e.weight>0 && config.catalog[e.spiritIndex].rarity==rarity))
                {error="排出候補のないレアリティがあります: ★"+rarity;return false;}
            return true;
        }
        public static int Draw(HubConfig config,GachaBanner banner,Random random)
        {
            var pool=banner.pool;
            if(UsesRarity(banner))
            {
                int roll=random.Next(100),rarity=1;
                for(;rarity<=5;rarity++) {if(roll<banner.rarityWeights[rarity-1])break;roll-=banner.rarityWeights[rarity-1];}
                pool=pool.Where(e=>config.catalog[e.spiritIndex].rarity==rarity).ToArray();
            }
            int pick=random.Next(pool.Sum(e=>e.weight));
            foreach(var entry in pool) {if(pick<entry.weight)return entry.spiritIndex;pick-=entry.weight;}
            throw new InvalidOperationException("Invalid gacha pool");
        }
        public static double Probability(HubConfig config,GachaBanner banner,int id)
        {
            int rarity=config.catalog[id].rarity;
            var pool=UsesRarity(banner)?banner.pool.Where(e=>config.catalog[e.spiritIndex].rarity==rarity):banner.pool;
            long total=pool.Sum(e=>(long)e.weight);
            if(total==0)return 0;
            double tier=UsesRarity(banner)?banner.rarityWeights[rarity-1]/100.0:1;
            return tier*pool.Where(e=>e.spiritIndex==id).Sum(e=>(long)e.weight)/total;
        }
    }
}
