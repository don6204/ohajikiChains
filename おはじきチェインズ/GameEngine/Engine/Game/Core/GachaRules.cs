#nullable disable
// Ported from the Unity game. Rendering and engine primitives are provided by DirectX 12 / PortableTypes.
using System;
using System.Globalization;
using System.Linq;

namespace Ohajiki
{
    public static class GachaRules
    {
        public static bool UsesRarity(GachaBanner banner) => banner.rarityWeights!=null && banner.rarityWeights.Length>0;
        public static bool UsesPlays(GachaBanner banner) => banner.plays!=null && banner.plays.Length>0;
        public static bool Validate(HubConfig config,GachaBanner banner,out string error)
        {
            error=null;
            if(!Window(banner,out _,out _)) {error="ガチャ期間が不正です";return false;}
            if(UsesPlays(banner))
            {
                if(banner.plays.Any(p=>p==null || string.IsNullOrWhiteSpace(p.id)) ||
                    banner.plays.Select(p=>p.id).Distinct(StringComparer.Ordinal).Count()!=banner.plays.Length ||
                    banner.plays.Select(p=>p.order).Distinct().Count()!=banner.plays.Length)
                {error="ガチャプレイIDが空欄、重複、または一覧が空です";return false;}
                foreach(var play in banner.plays)
                {
                    if(play.order<1 || play.cost<0 || (play.drawCount!=1 && play.drawCount!=10) ||
                        play.purchaseLimit<0 || play.resetPeriod<0 || play.resetPeriod>4)
                    {error="ガチャプレイ設定が不正です: "+play.id;return false;}
                    if(play.paidOnly)
                    {error="有償宝石専用の抽選には有償・無償残高の分離が必要です: "+play.id;return false;}
                }
            }
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
            =>DrawEntry(config,banner,random).spiritIndex;
        public static GachaEntry DrawEntry(HubConfig config,GachaBanner banner,Random random)
        {
            var pool=banner.pool;
            if(UsesRarity(banner))
            {
                int roll=random.Next(100),rarity=1;
                for(;rarity<=5;rarity++) {if(roll<banner.rarityWeights[rarity-1])break;roll-=banner.rarityWeights[rarity-1];}
                pool=pool.Where(e=>config.catalog[e.spiritIndex].rarity==rarity).ToArray();
            }
            int pick=random.Next(pool.Sum(e=>e.weight));
            foreach(var entry in pool) {if(pick<entry.weight)return entry;pick-=entry.weight;}
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
        // Workbook dates retain their time of day. Bare local dates use the game's calendar offset.
        public static bool IsOpen(GachaBanner banner,DateTimeOffset now) => Window(banner,out var start,out var end) && now>=start && now<end;
        static bool Window(GachaBanner banner,out DateTimeOffset start,out DateTimeOffset end)
        {
            start=DateTimeOffset.MinValue;end=DateTimeOffset.MaxValue;
            return (string.IsNullOrEmpty(banner.availableFrom)||ParseDate(banner.availableFrom,out start)) &&
                (string.IsNullOrEmpty(banner.availableUntil)||ParseDate(banner.availableUntil,out end)) && start<end;
        }
        static bool ParseDate(string value,out DateTimeOffset date)
        {
            if(DateTime.TryParseExact(value,new[]{"yyyy-MM-dd","yyyy-MM-dd'T'HH:mm:ss","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"},
                CultureInfo.InvariantCulture,DateTimeStyles.None,out var local))
            {
                try
                {
                    date=new DateTimeOffset(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),ShopCalendar.LocalTime(DateTimeOffset.UtcNow).Offset);
                    return true;
                }
                catch(ArgumentException){date=default;return false;}
            }
            return DateTimeOffset.TryParseExact(value,new[]{"O","yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"},
                CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
        }
    }
}
