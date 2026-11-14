using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ohajiki
{
    public sealed class ShopQuote
    {
        public ShopProduct Product {get;}
        internal string Fingerprint {get;}
        internal DateTime Day {get;}
        internal ShopQuote(ShopProduct product,DateTime day)
        { Product=JsonUtility.FromJson<ShopProduct>(JsonUtility.ToJson(product));Fingerprint=JsonUtility.ToJson(product);Day=day; }
    }
    // Local prototype only: no store SDK, billing call or automatic subscription renewal.
    public sealed class ShopState
    {
        readonly PlayerSession player;
        readonly Func<DateTimeOffset> clock;
        readonly Dictionary<string,int> purchases=new Dictionary<string,int>();
        ShopQuote pending;
        DateTime premiumMonth, lastClaim, observedDay;
        public ShopConfig Config {get;}
        public DateTime Today => ShopCalendar.Today(clock());
        public bool PremiumActive => premiumMonth.Year==Today.Year && premiumMonth.Month==Today.Month;
        public bool EventOpen => Config.eventActive && ShopCalendar.IsOpen(Config.eventFrom,Config.eventUntil,Today);
        public ShopState(PlayerSession player,ShopConfig config,Func<DateTimeOffset> clock)
        {
            if(!config.Validate(out string error))throw new ArgumentException(error);
            this.player=player;Config=config;this.clock=clock;observedDay=Today;
        }
        public ShopProduct Find(string id) => Config.products.FirstOrDefault(p=>p.id==id);
        string LimitKey(ShopProduct p)
        {
            string period=p.limitPeriod==ShopLimitPeriod.Daily?Today.ToString("yyyy-MM-dd"):
                p.limitPeriod==ShopLimitPeriod.Monthly?Today.ToString("yyyy-MM"):"session";
            return p.id+":"+period;
        }
        public int Remaining(ShopProduct p)
        {
            if(p.category==ShopCategory.Premium)return PremiumActive?0:1;
            if(p.limitPeriod==ShopLimitPeriod.None)return int.MaxValue;
            purchases.TryGetValue(LimitKey(p),out int count);return Math.Max(0,p.purchaseLimit-count);
        }
        public bool CanPurchase(ShopProduct p,out string reason)
        {
            if(p==null || !p.enabled) {reason="現在は販売していません";return false;}
            if(p.category==ShopCategory.Event && !EventOpen) {reason="イベント開催前です";return false;}
            if(!ShopCalendar.IsOpen(p.availableFrom,p.availableUntil,Today)) {reason="販売期間外です";return false;}
            if(Remaining(p)==0) {reason=p.category==ShopCategory.Premium?"今月は購入済みです":"購入上限に達しました";return false;}
            return player.CanApplyShop(p.currency,p.price,p.rewards,out reason);
        }
        public ShopQuote Prepare(string id,out string reason)
        {
            pending=null;var product=Find(id);
            if(!CanPurchase(product,out reason))return null;
            pending=new ShopQuote(product,Today);return pending;
        }
        public void Cancel(ShopQuote quote) {if(ReferenceEquals(quote,pending))pending=null;}
        public bool Confirm(ShopQuote quote,out string message)
        {
            if(quote==null || !ReferenceEquals(quote,pending)) {message="この確認は終了しています";return false;}
            pending=null;var product=Find(quote.Product.id);
            if(product==null || quote.Fingerprint!=JsonUtility.ToJson(product) || quote.Fingerprint!=JsonUtility.ToJson(quote.Product) || quote.Day!=Today)
            {message="商品情報または日付が変わりました。もう一度確認してください。";return false;}
            if(!CanPurchase(product,out message) || !player.ApplyShop(product.currency,product.price,product.rewards,out message))return false;
            string key=LimitKey(product);purchases.TryGetValue(key,out int used);purchases[key]=used+1;
            if(product.category==ShopCategory.Premium)
            {premiumMonth=new DateTime(Today.Year,Today.Month,1);ClaimDailyBonus();}
            message=product.currency==ShopCurrency.SimulatedYen?"仮購入を反映しました。実際の請求はありません。":"交換しました。";
            return true;
        }
        public bool ClaimDailyBonus()
        {
            if(!PremiumActive || lastClaim>=Today)return false;
            var bonus=new[]{new ShopReward{kind=ShopRewardKind.Gems,amount=Config.premiumDailyGems}};
            if(!player.ApplyShop(ShopCurrency.SimulatedYen,0,bonus,out _))return false;
            lastClaim=Today;return true;
        }
        public bool RefreshCalendar()
        {
            bool changed=observedDay!=Today;observedDay=Today;
            return ClaimDailyBonus()||changed;
        }
        internal ShopSaveData CaptureSave()=>new ShopSaveData{
            purchases=purchases.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new CountSaveData{key=p.Key,count=p.Value}).ToArray(),
            premiumMonthTicks=premiumMonth.Ticks,lastClaimTicks=lastClaim.Ticks};
        internal void RestoreSave(ShopSaveData data)
        {
            if(data==null)return;
            purchases.Clear();foreach(var value in data.purchases??Array.Empty<CountSaveData>())purchases[value.key]=value.count;
            premiumMonth=new DateTime(data.premiumMonthTicks);lastClaim=new DateTime(data.lastClaimTicks);
            observedDay=Today;pending=null;
        }
    }
}
