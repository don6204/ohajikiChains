using System;
using System.Linq;
using UnityEngine;

namespace Ohajiki
{
    public sealed partial class HubView
    {
        bool purchaseMode;
        int shopTab;
        static readonly string[] rewardNames={"メダル","宝石","火パワー","水パワー","木パワー","全パワー","輝く石","イベントポイント"};
        string RewardText(ShopProduct product) => product.category==ShopCategory.Premium?
            $"毎日 宝石{session.Shop.Config.premiumDailyGems}個 / 購入月の月末まで":
            string.Join(" / ",product.rewards.Select(r=>$"{rewardNames[(int)r.kind]} {r.amount:N0}"));
        static string PriceText(ShopProduct product)
        {
            if(product.currency==ShopCurrency.SimulatedYen)return $"価格 {product.price:N0}円";
            return new[]{"メダル","宝石","輝く石","イベントポイント"}[(int)product.currency]+$" {product.price:N0}";
        }
        string LimitText(ShopProduct product)
        {
            int remaining=session.Shop.Remaining(product);
            string limit=remaining==int.MaxValue?"回数制限なし":$"残り {remaining}回";
            if(product.limitPeriod==ShopLimitPeriod.Daily)limit+=" / 本日";
            if(product.limitPeriod==ShopLimitPeriod.Monthly || product.category==ShopCategory.Premium)limit+=" / 今月";
            if(!string.IsNullOrEmpty(product.availableUntil))limit+=$" / {product.availableUntil} 0時まで";
            return limit;
        }
        void ShopPage()
        {
            var shop=session.Shop;
            Heading("ショップ",$"輝く石 {session.ShiningStones:N0}　イベントポイント {session.EventPoints:N0}");
            if(shop==null) {Message("ショップデータが未設定です");return;}
            ui.Button(content,"交換",0,768,320,60,()=>{purchaseMode=false;shopTab=0;Render();},true,purchaseMode?HubWidgets.PanelColor:HubWidgets.Accent);
            ui.Button(content,"購入",336,768,320,60,()=>{purchaseMode=true;shopTab=0;Render();},true,purchaseMode?HubWidgets.Accent:HubWidgets.PanelColor);
            string[] tabs=purchaseMode?new[]{"オススメ","パック購入","宝石購入","プレミアム"}:new[]{"アイテム","メダル","宝石交換","イベント"};
            for(int i=0;i<4;i++)
            {
                int index=i;bool open=purchaseMode || i!=3 || shop.EventOpen;
                ui.Button(content,tabs[i]+(!open?"\n開催前":""),i*164,684,152,62,()=>{shopTab=index;Render();},open,shopTab==i?HubWidgets.Accent:HubWidgets.PanelColor);
            }
            if(purchaseMode && shopTab==3) {PremiumPage();return;}
            var category=purchaseMode?(shopTab==1?ShopCategory.Packs:ShopCategory.GemsPurchase):
                new[]{ShopCategory.Items,ShopCategory.Medals,ShopCategory.GemsExchange,ShopCategory.Event}[shopTab];
            var products=(purchaseMode && shopTab==0?shop.Config.products.Where(p=>p.recommended && p.currency==ShopCurrency.SimulatedYen):
                shop.Config.products.Where(p=>p.category==category)).Where(p=>p.enabled).OrderBy(p=>p.discounted).ToArray();
            var list=ui.Scroll(content,0,20,656,642);
            if(products.Length==0)
            {var row=ui.Row(list,160);ui.Label(row,"商品を準備中です",0,0,640,160,26,HubWidgets.Muted,true);}
            foreach(var product in products)ShopRow(list,product,purchaseMode && shopTab<=1);
            Message(purchaseMode?"テスト購入です。確認後に付与され、実際の請求はありません。":"交換するたびに、確認画面を表示します。");
        }
        void ShopRow(Transform list,ShopProduct product,bool banner)
        {
            float height=banner?254:188;
            var row=ui.Row(list,height);
            var panel=ui.Panel(row,"Product_"+product.id,0,0,640,height,HubWidgets.PanelColor);
            skin.Image(panel,"Product accent",skin.Shard,610,height-31,15,22,SpiritUiSkin.Gold,true);
            if(banner && product.portraitIndex>=0 && product.portraitIndex<session.Config.catalog.Length)
                ui.Portrait(panel,session.Config.catalog[product.portraitIndex].portrait,430,86,180,160);
            ui.Label(panel,product.title,20,height-57,banner?415:600,45,27);
            ui.Label(panel,RewardText(product),20,height-112,banner?415:600,48,19,HubWidgets.Muted);
            if(banner)ui.Label(panel,product.discounted && product.originalPrice>product.price?$"通常 {product.originalPrice:N0}円 → 割引中":product.description,20,97,400,40,17,HubWidgets.Muted);
            ui.Label(panel,PriceText(product),20,39,360,44,24);
            ui.Label(panel,LimitText(product),20,5,400,30,15,HubWidgets.Muted);
            bool available=session.Shop.CanPurchase(product,out string reason);
            ui.Button(panel,available?(purchaseMode?"購入する":"交換する"):reason,430,18,190,62,()=>ShowShopConfirmation(product.id),available,HubWidgets.Accent);
        }
        void PremiumPage()
        {
            var shop=session.Shop;var product=shop.Config.products.FirstOrDefault(p=>p.category==ShopCategory.Premium);
            if(product==null) {Message("プレミアムパスは準備中です");return;}
            var banner=ui.Panel(content,"PremiumPass",0,303,656,350,HubWidgets.PanelColor);
            skin.Halo(banner,new Vector2(328,190),280,new Color(.95f,.66f,.14f,.35f));
            ui.Label(banner,"PREMIUM PASS",28,254,600,60,37,null,true);
            ui.Label(banner,$"毎日 宝石 {shop.Config.premiumDailyGems}個",28,168,600,60,31,null,true);
            ui.Label(banner,"購入月の月末まで有効\n毎月1日更新 / 自動更新なし",28,55,600,95,22,HubWidgets.Muted,true);
            ui.Label(content,$"{shop.Today:yyyy年M月}分　{product.price:N0}円",0,218,656,54,30,null,true);
            bool available=shop.CanPurchase(product,out string reason);
            ui.Button(content,shop.PremiumActive?"購入済み":available?"プレミアムパスを購入":reason,0,106,656,80,()=>ShowShopConfirmation(product.id),available,HubWidgets.Accent);
            ui.Label(content,shop.PremiumActive?"本日のログインボーナスを受け取りました。":"購入当日から、その日にログインすると宝石を付与。",0,37,656,52,18,HubWidgets.Muted,true);
            Message("仮購入です。パスと受取履歴はこの端末に保存されます。");
        }
        public void ShowShopConfirmation(string id)
        {
            var quote=session.Shop.Prepare(id,out string reason);
            if(quote==null) {Message(reason);return;}
            // Close the old modal before attaching this quote's cancellation callback.
            OpenModal("ShopConfirmation",new Color(0,0,0,.9f));
            modalCancelled=()=>session.Shop.Cancel(quote);
            var product=quote.Product;bool cash=product.currency==ShopCurrency.SimulatedYen;
            var box=ui.Panel(modal,"Dialog",48,300,624,680,HubWidgets.PanelColor,true);
            ui.Label(box,cash?"この商品を仮購入しますか？":"このアイテムと交換しますか？",24,570,576,80,29,null,true);
            ui.Label(box,product.title,24,475,576,80,27,null,true);
            ui.Label(box,RewardText(product),30,355,564,105,23,HubWidgets.Muted,true);
            ui.Label(box,PriceText(product),24,282,576,60,31,null,true);
            ui.Label(box,LimitText(product),24,228,576,46,19,HubWidgets.Muted,true);
            ui.Label(box,cash?"テスト購入 / 実際の請求はありません\n正式な販売情報は未設定です":"表示された通貨・アイテムを消費します。",24,151,576,69,18,HubWidgets.Muted,true);
            if(cash)ui.Button(box,"特定商取引法に基づく表記",80,106,464,40,()=>ShowLegalNotice(()=>ShowShopConfirmation(id)));
            ui.Button(box,"いいえ",28,28,250,76,CloseModal);
            ui.Button(box,"はい",304,28,292,76,()=>{
                DismissModal(false);
                bool ok=session.Shop.Confirm(quote,out string message);Render();
                if(ok)ShowShopResult(product,message);else Message(message);
            },true,HubWidgets.Accent);
        }
        void ShowShopResult(ShopProduct product,string message)
        {
            OpenModal("ShopResult",new Color(.025f,.07f,.1f,1));
            ui.Label(modal,product.currency==ShopCurrency.SimulatedYen?"購入を反映しました":"交換しました",48,980,624,100,38,null,true);
            ui.Label(modal,product.title,48,837,624,100,30,null,true);
            ui.Label(modal,RewardText(product),48,650,624,150,27,HubWidgets.Muted,true);
            ui.Label(modal,$"メダル {session.Progress.Coins:N0}\n宝石 {session.Gems:N0}　輝く石 {session.ShiningStones:N0}",48,450,624,150,27,null,true);
            ui.Label(modal,message,48,300,624,100,21,HubWidgets.Muted,true);
            ui.Button(modal,"閉じる",160,160,400,80,()=>{CloseModal();Render();},true,HubWidgets.Accent);
        }
    }
}
