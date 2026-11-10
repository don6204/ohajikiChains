using System;

namespace Ohajiki
{
    public sealed partial class PlayerSession
    {
        public bool CanApplyShop(ShopCurrency currency,int price,ShopReward[] rewards,out string reason)
            => PlanShop(currency,price,rewards,out _,out reason);
        public bool ApplyShop(ShopCurrency currency,int price,ShopReward[] rewards,out string reason)
        {
            if(!PlanShop(currency,price,rewards,out long[] balances,out reason))return false;
            Progress.SetShopCoins(balances[0]);Gems=(int)balances[1];ShiningStones=(int)balances[2];EventPoints=(int)balances[3];
            for(int i=0;i<4;i++)materials[i]=(int)balances[4+i];
            return true;
        }
        bool PlanShop(ShopCurrency currency,int price,ShopReward[] rewards,out long[] balances,out string reason)
        {
            balances=new long[]{Progress.Coins,Gems,ShiningStones,EventPoints,materials[0],materials[1],materials[2],materials[3]};
            reason=null;
            if(price<0 || rewards==null || !Enum.IsDefined(typeof(ShopCurrency),currency))
            {reason="商品設定が不正です";return false;}
            if(currency!=ShopCurrency.SimulatedYen)
            {
                int debit=(int)currency;
                if(balances[debit]<price) {reason=new[]{"メダル","宝石","輝く石","イベントポイント"}[debit]+"が不足しています";return false;}
                balances[debit]-=price;
            }
            foreach(var reward in rewards)
            {
                if(reward==null || reward.amount<=0 || !Enum.IsDefined(typeof(ShopRewardKind),reward.kind))
                {reason="付与内容が不正です";return false;}
                int credit=reward.kind==ShopRewardKind.Medals?0:reward.kind==ShopRewardKind.Gems?1:
                    reward.kind==ShopRewardKind.ShiningStones?2:reward.kind==ShopRewardKind.EventPoints?3:(int)reward.kind+2;
                if(balances[credit]>long.MaxValue-reward.amount) {reason="所持数の上限です";return false;}
                balances[credit]+=reward.amount;
            }
            for(int i=1;i<balances.Length;i++)if(balances[i]>int.MaxValue) {reason="所持数の上限です";return false;}
            return true;
        }
    }
}
