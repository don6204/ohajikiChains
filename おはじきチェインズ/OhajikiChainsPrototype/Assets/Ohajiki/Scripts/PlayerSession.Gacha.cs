using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Ohajiki
{
    // Immutable receipt: playback, skipping and advancing never draw or grant again.
    public sealed class GachaReceipt
    {
        public IReadOnlyList<int> SpiritIds { get; }
        public IReadOnlyList<bool> NewSpirits { get; }
        public IReadOnlyList<int> Quantities { get; }
        public int Cost { get; }
        public int Bonus { get; }
        public int Banner { get; }
        internal GachaReceipt(int banner,int[] ids,bool[] fresh,int[] quantities,int cost,int bonus)
        {Banner=banner;SpiritIds=Array.AsReadOnly(ids);NewSpirits=Array.AsReadOnly(fresh);Quantities=Array.AsReadOnly(quantities);Cost=cost;Bonus=bonus;}
    }
    public sealed partial class PlayerSession
    {
        readonly HashSet<int> usedSingleDiscounts = new HashSet<int>();
        readonly Dictionary<string,int> gachaPlayUses=new Dictionary<string,int>(StringComparer.Ordinal);
        public bool FirstSingleUsed(int banner)
        {
            var definition=Config.banners[banner];
            if(!GachaRules.UsesPlays(definition))return usedSingleDiscounts.Contains(banner);
            var first=definition.plays.Where(p=>p.drawCount==1).OrderBy(p=>p.order).FirstOrDefault();
            return first==null || first.purchaseLimit==0 || PlayUses(banner,first,ShopCalendar.Today(clock()))>=first.purchaseLimit;
        }
        public int GachaCost(int banner,int count)
        {
            if(count!=1 && count!=10)throw new ArgumentOutOfRangeException(nameof(count));
            var definition=Config.banners[banner];
            if(!GachaRules.UsesPlays(definition))
                return count==1?(FirstSingleUsed(banner)?Config.singleGems:Config.firstSingleGems):Config.tenGems;
            // Keep the advertised price available when a limited option is exhausted; CanDraw supplies the reason.
            return (AvailablePlay(banner,count,ShopCalendar.Today(clock()))??definition.plays.Where(p=>p.drawCount==count).OrderBy(p=>p.order).FirstOrDefault())?.cost??0;
        }
        GachaPlayOption AvailablePlay(int banner,int count,DateTime date)=>Config.banners[banner].plays
            .Where(p=>p.drawCount==count && (p.purchaseLimit==0 || PlayUses(banner,p,date)<p.purchaseLimit))
            .OrderBy(p=>p.order).ThenBy(p=>p.id,StringComparer.Ordinal).FirstOrDefault();
        int PlayUses(int banner,GachaPlayOption play,DateTime date) => gachaPlayUses.TryGetValue(PlayKey(banner,play,date),out int uses)?uses:0;
        string PlayKey(int banner,GachaPlayOption play,DateTime date)
        {
            string period="session";
            if(play.resetPeriod==1)period=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            else if(play.resetPeriod==2)period=date.AddDays(-((int)date.DayOfWeek+6)%7).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            else if(play.resetPeriod==3)period=date.ToString("yyyy-MM",CultureInfo.InvariantCulture);
            else if(play.resetPeriod==4)period=date.ToString("yyyy",CultureInfo.InvariantCulture);
            string bannerId=Config.banners[banner].masterId;
            return (string.IsNullOrEmpty(bannerId)?banner.ToString(CultureInfo.InvariantCulture):bannerId)+"|"+play.id+"|"+period;
        }
        public bool CanDraw(int banner,int count,out string error)=>TryGachaQuote(banner,count,out _,out _,out _,out _,out error);
        bool TryGachaQuote(int banner,int count,out int cost,out int bonus,out GachaPlayOption play,out string playKey,out string error)
        {
            error=null;cost=bonus=0;play=null;playKey=null;
            if(count!=1 && count!=10){error="1連または10連を選んでください";return false;}
            // Capture one instant for availability, cost selection and the limit bucket used by this commit.
            DateTimeOffset now=clock();
            if(banner<0 || banner>=Config.banners.Length || !Config.banners[banner].IsOpen(now))
            {error="ガチャ開催期間外です";return false;}
            if(GachaRules.UsesPlays(Config.banners[banner]))
            {
                DateTime date=ShopCalendar.Today(now);
                play=AvailablePlay(banner,count,date);
                if(play==null){error="この抽選の利用回数が上限に達しているか、未設定です";return false;}
                if(play.paidOnly){error="有償宝石専用の抽選は未対応です";return false;}
                cost=play.cost;playKey=PlayKey(banner,play,date);
            }
            else cost=count==1?(usedSingleDiscounts.Contains(banner)?Config.singleGems:Config.firstSingleGems):Config.tenGems;
            bonus=count==10?Config.tenBonusGems:0;
            if(Gems<cost){error=$"宝石が不足しています（必要 {cost} / 所持 {Gems}）";return false;}
            if((long)Gems-cost+bonus>int.MaxValue){error="宝石の所持上限です";return false;}
            return true;
        }
        public bool TryDraw(int banner,int count,out GachaReceipt receipt,out string error)
        {
            receipt=null;
            if(!TryGachaQuote(banner,count,out int cost,out int bonus,out var play,out string playKey,out error))return false;
            var ids=new int[count];var fresh=new bool[count];var quantities=new int[count];var additions=new Dictionary<int,int>();
            for(int i=0;i<count;i++)
            {
                var entry=GachaRules.DrawEntry(Config,Config.banners[banner],random);
                int id=entry.spiritIndex;
                additions.TryGetValue(id,out int prior);
                if((long)copies[id]+prior+entry.quantity>int.MaxValue){error="キャラの所持上限です";return false;}
                ids[i]=id;fresh[i]=copies[id]==0 && prior==0;quantities[i]=entry.quantity;additions[id]=prior+entry.quantity;
            }
            // All validation precedes this one synchronous commit.
            Gems=Gems-cost+bonus;
            foreach(var pair in additions)copies[pair.Key]+=pair.Value;
            if(count==1)usedSingleDiscounts.Add(banner);
            if(playKey!=null && play.purchaseLimit>0)
            {gachaPlayUses.TryGetValue(playKey,out int used);gachaPlayUses[playKey]=used+1;}
            receipt=new GachaReceipt(banner,ids,fresh,quantities,cost,bonus);return true;
        }
    }
}
