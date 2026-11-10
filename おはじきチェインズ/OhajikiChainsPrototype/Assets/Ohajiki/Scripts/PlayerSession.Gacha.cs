using System;
using System.Collections.Generic;
namespace Ohajiki
{
    // Immutable receipt: playback, skipping and advancing never draw or grant again.
    public sealed class GachaReceipt
    {
        public IReadOnlyList<int> SpiritIds { get; }
        public IReadOnlyList<bool> NewSpirits { get; }
        public int Cost { get; }
        public int Bonus { get; }
        public int Banner { get; }
        internal GachaReceipt(int banner,int[] ids,bool[] fresh,int cost,int bonus)
        {Banner=banner;SpiritIds=Array.AsReadOnly(ids);NewSpirits=Array.AsReadOnly(fresh);Cost=cost;Bonus=bonus;}
    }
    public sealed partial class PlayerSession
    {
        public bool FirstSingleUsed { get; private set; }
        public int GachaCost(int count) => count==1?(FirstSingleUsed?Config.singleGems:Config.firstSingleGems):
            count==10?Config.tenGems:throw new ArgumentOutOfRangeException(nameof(count));
        public bool CanDraw(int banner,int count,out string error)
        {
            error=null;
            if(count!=1 && count!=10){error="1連または10連を選んでください";return false;}
            if(!BannerOpen(banner)){error="ガチャ開催期間外です";return false;}
            int cost=GachaCost(count),bonus=count==10?Config.tenBonusGems:0;
            if(Gems<cost){error=$"宝石が不足しています（必要 {cost} / 所持 {Gems}）";return false;}
            if((long)Gems-cost+bonus>int.MaxValue){error="宝石の所持上限です";return false;}
            return true;
        }
        public bool TryDraw(int banner,int count,out GachaReceipt receipt,out string error)
        {
            receipt=null;
            if(!CanDraw(banner,count,out error))return false;
            int cost=GachaCost(count),bonus=count==10?Config.tenBonusGems:0;
            var ids=new int[count];var fresh=new bool[count];var additions=new Dictionary<int,int>();
            for(int i=0;i<count;i++)
            {
                int id=GachaRules.Draw(Config,Config.banners[banner],random);
                additions.TryGetValue(id,out int prior);
                if((long)copies[id]+prior+1>int.MaxValue){error="キャラの所持上限です";return false;}
                ids[i]=id;fresh[i]=copies[id]==0 && prior==0;additions[id]=prior+1;
            }
            // All validation precedes this one synchronous commit.
            Gems=Gems-cost+bonus;
            foreach(var pair in additions)copies[pair.Key]+=pair.Value;
            if(count==1)FirstSingleUsed=true;
            receipt=new GachaReceipt(banner,ids,fresh,cost,bonus);return true;
        }
    }
}
