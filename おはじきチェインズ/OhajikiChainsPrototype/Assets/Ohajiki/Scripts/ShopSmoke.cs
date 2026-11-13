using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Ohajiki
{
    public sealed class ShopSmoke : MonoBehaviour
    {
        PrototypeApp app;
        string directory;
        HubConfig calendarConfig;GachaBanner[] originalBanners,calendarBanners;
        public void Run(PrototypeApp target) {app=target;StartCoroutine(Guard(Flow()));}
        void UseOpenCalendarFixture()
        {
            // Calendar correctness is covered separately; this smoke must reach the shop-to-gacha purchase flow.
            calendarConfig=app.Session.Config;originalBanners=calendarConfig.banners;
            calendarBanners=originalBanners.Select(b=>{
                var copy=JsonUtility.FromJson<GachaBanner>(JsonUtility.ToJson(b));
                copy.availableFrom=copy.availableUntil=null;return copy;
            }).ToArray();
            calendarConfig.banners=calendarBanners;
        }
        void RestoreCalendarFixture()
        {
            if(calendarConfig!=null&&ReferenceEquals(calendarConfig.banners,calendarBanners))calendarConfig.banners=originalBanners;
            calendarConfig=null;originalBanners=calendarBanners=null;
        }
        void OnDisable()=>RestoreCalendarFixture();
        IEnumerator Guard(IEnumerator flow)
        {
            while(true)
            {
                object next;
                try {if(!flow.MoveNext())yield break;next=flow.Current;}
                catch(Exception error) {RestoreCalendarFixture();Debug.LogError("SHOP SMOKE FAIL: "+error);Application.Quit(20);yield break;}
                yield return next;
            }
        }
        void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
        UnityEngine.UI.Button Button(string title) => app.Hub.GetComponentsInChildren<UnityEngine.UI.Button>()
            .LastOrDefault(b=>b.IsInteractable() && b.name=="Button_"+title);
        void Click(string title) {var b=Button(title);Require(b!=null,"Missing button: "+title);b.onClick.Invoke();}
        void Product(string id)
        {
            var panel=app.Hub.GetComponentsInChildren<RectTransform>().FirstOrDefault(t=>t.name=="Product_"+id);
            Require(panel!=null,"Missing product: "+id);
            var b=panel.GetComponentInChildren<UnityEngine.UI.Button>();Require(b!=null && b.IsInteractable(),"Product blocked: "+id);b.onClick.Invoke();
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();UiSmokeChecks.Validate(app.Hub.gameObject);ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));
            yield return null;yield return new WaitForEndOfFrame();
        }
        IEnumerator Flow()
        {
            directory=Path.Combine(Application.dataPath,"..","SmokeShop");Directory.CreateDirectory(directory);
            yield return new WaitForSeconds(.8f);
            // Initialize the hidden player's swap chain before collecting visual evidence.
            Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
            var session=app.Session;long coins=session.Progress.Coins;
            UseOpenCalendarFixture();
            int gems=session.Gems,fire=session.MaterialCount(0),stones=session.ShiningStones;
            bool eventActive=session.Config.shop.eventActive;session.Config.shop.eventActive=false;
            Click("ショップ");yield return Capture("01-exchange");
            var locked=app.Hub.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(b=>b.name=="Button_イベント\n開催前");
            Require(locked!=null&&!locked.IsInteractable(),"event locked");
            session.Config.shop.eventActive=eventActive;app.Hub.Refresh();
            Product("power_0");yield return Capture("02-exchange-confirm");Click("いいえ");
            Require(session.Progress.Coins==coins && session.MaterialCount(0)==fire,"cancel exchange");
            Product("power_0");Click("はい");Require(session.Progress.Coins==coins-500 && session.MaterialCount(0)==fire+1,"power exchange");Click("閉じる");
            Click("宝石交換");Product("gem_from_stones");Click("はい");
            Require(session.Gems==gems+1 && session.ShiningStones==stones-5,"stone exchange");Click("閉じる");
            Click("購入");yield return Capture("03-recommended");
            Click("宝石購入");yield return Capture("04-gem-products");Product("gems_32");
            yield return Capture("05-purchase-confirm");Click("いいえ");Require(session.Gems==gems+1,"cancel purchase");
            Product("gems_32");Click("はい");Require(session.Gems==gems+33 && session.Progress.Coins==coins-500,"simulated gem purchase");
            yield return Capture("06-purchase-result");Click("閉じる");
            Click("交換");Click("メダル");Product("medals_1000");Click("はい");Click("閉じる");
            Require(session.Gems==gems+28 && session.Progress.Coins==coins+500,"gem to medals");
            Click("購入");Product("release_0");Click("はい");Click("閉じる");
            Require(session.MaterialCount(0)==fire+21 && session.Gems==gems+38,"pack contents");
            Click("パック購入");Require(!session.Shop.CanPurchase(session.Shop.Find("release_0"),out _),"shared pack limit");
            yield return Capture("07-pack-limit");
            Click("プレミアム");yield return Capture("08-premium-before");Click("プレミアムパスを購入");Click("はい");Click("閉じる");
            Require(session.Shop.PremiumActive && session.Gems==gems+40,"premium activation and daily bonus");
            Require(Button("購入済み")==null,"no duplicate premium");yield return Capture("09-premium-after");
            Click("ガチャ");yield return null;
            var banner=app.Hub.GetComponentsInChildren<UnityEngine.UI.Button>().First(b=>b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="有償ガチャ（試作）"));
            banner.onClick.Invoke();Click("排出率・排出内容");yield return Capture("10-paid-rates");Click("閉じる");
            app.Hub.RequestGacha(1);Click("決定");Require(session.Gems==gems+39,"shared gems and first discount");
            app.Hub.GachaMovie.Skip();yield return new WaitForSeconds(.6f);app.Hub.GachaMovie.Next();yield return null;
            Click("ホーム");yield return Capture("11-home");
            RestoreCalendarFixture();
            Debug.Log("SHOP SMOKE PASS: locked event, exchange/purchase confirm and cancel, gem/medal/stone balances, pack rewards/shared limit, monthly premium/daily bonus, paid banner rarity display and gem-cost summon, return home.");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--stay-open")<0)Application.Quit(0);
        }
    }
}
