using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Ohajiki
{
    public enum ShopCategory { Items, Medals, GemsExchange, Event, Packs, GemsPurchase, Premium }
    public enum ShopCurrency { Medals, Gems, ShiningStones, EventPoints, SimulatedYen }
    public enum ShopRewardKind { Medals, Gems, FirePower, WaterPower, WoodPower, AllPower, ShiningStones, EventPoints }
    public enum ShopLimitPeriod { None, Session, Daily, Monthly }

    [Serializable] public sealed class ShopReward
    {
        public ShopRewardKind kind;
        [Min(1)] public int amount=1;
    }
    [Serializable] public sealed class ShopProduct
    {
        public string id;
        public string title;
        [TextArea] public string description;
        public ShopCategory category;
        public ShopCurrency currency;
        [Min(1)] public int price=1;
        public ShopReward[] rewards=Array.Empty<ShopReward>();
        public bool recommended;
        public bool discounted;
        [Min(0)] public int originalPrice;
        public int portraitIndex=-1;
        public bool enabled=true;
        [Tooltip("日本時間 yyyy-MM-dd。空欄なら制限なし。終了日はその日を含まない")]
        public string availableFrom, availableUntil;
        public ShopLimitPeriod limitPeriod;
        [Min(0)] public int purchaseLimit;
    }
    [CreateAssetMenu(menuName="Ohajiki/Shop Config")]
    public sealed class ShopConfig : ScriptableObject
    {
        [Header("起動中のみ保持する仮購入・交換")]
        [Min(0)] public int startingShiningStones=20;
        [Min(0)] public int startingEventPoints;
        public bool eventActive;
        public string eventFrom,eventUntil;
        [Min(1)] public int premiumDailyGems=2;
        public ShopProduct[] products=Array.Empty<ShopProduct>();
        public bool Validate(out string error)
        {
            if(startingShiningStones<0 || startingEventPoints<0 || premiumDailyGems<1 || products==null)
            { error="ショップ初期値が不正です";return false; }
            if(!ShopCalendar.ValidWindow(eventFrom,eventUntil)) {error="イベント期間が不正です";return false;}
            if(products.Any(p=>p==null || string.IsNullOrWhiteSpace(p.id)) || products.Select(p=>p.id).Distinct().Count()!=products.Length)
            {error="商品IDが未設定または重複しています";return false;}
            foreach(var p in products)
            {
                if(string.IsNullOrWhiteSpace(p.title) || p.price<1 || p.purchaseLimit<0 ||
                    (p.limitPeriod!=ShopLimitPeriod.None && p.purchaseLimit<1) || !ShopCalendar.ValidWindow(p.availableFrom,p.availableUntil) ||
                    p.rewards==null || p.rewards.Any(r=>r==null || r.amount<1 || !Enum.IsDefined(typeof(ShopRewardKind),r.kind)) ||
                    !Enum.IsDefined(typeof(ShopCurrency),p.currency) || !Enum.IsDefined(typeof(ShopCategory),p.category) ||
                    !Enum.IsDefined(typeof(ShopLimitPeriod),p.limitPeriod))
                {error="商品設定が不正です: "+p.id;return false;}
                if(p.category==ShopCategory.Premium && (p.currency!=ShopCurrency.SimulatedYen || p.rewards.Length!=0))
                {error="プレミアムパスは仮購入専用です: "+p.id;return false;}
            }
            error=null;return true;
        }
    }
    public static class ShopCalendar
    {
        public static DateTime Today(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(9)).Date;
        static bool Parse(string value,out DateTime date) => DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
        public static bool ValidWindow(string from,string until)
        {
            DateTime start=DateTime.MinValue,end=DateTime.MaxValue;
            return (string.IsNullOrEmpty(from)||Parse(from,out start)) && (string.IsNullOrEmpty(until)||Parse(until,out end)) && start<end;
        }
        public static bool IsOpen(string from,string until,DateTime day)
        {
            if(!ValidWindow(from,until))return false;
            return (string.IsNullOrEmpty(from)||day>=DateTime.ParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture)) &&
                (string.IsNullOrEmpty(until)||day<DateTime.ParseExact(until,"yyyy-MM-dd",CultureInfo.InvariantCulture));
        }
    }
}
