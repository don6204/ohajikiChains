using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ohajiki
{
    // Explicit validation mode. Never attached during normal startup.
    public sealed class TitleSmoke : MonoBehaviour
    {
        PrototypeApp app;
        string output;
        public void Run(PrototypeApp owner){app=owner;StartCoroutine(Guard(Flow()));}
        IEnumerator Guard(IEnumerator flow)
        {
            while(true)
            {
                object next;
                try {if(!flow.MoveNext())yield break;next=flow.Current;}
                catch(Exception e){Debug.LogError("TITLE SMOKE FAIL: "+e);Application.Quit(25);yield break;}
                yield return next;
            }
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        UnityEngine.UI.Button Button(string name)=>app.Title.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="Button_"+name);
        UnityEngine.UI.Button HubButton(string name)=>app.Hub.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="Button_"+name);
        void Click(UnityEngine.UI.Button button)
        {
            ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        }
        void Capture(string name)
        {
            var root=app.Title!=null?app.Title.gameObject:app.Hub.gameObject;
            UiSmokeChecks.Validate(root);
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"one active EventSystem");
            Require(!root.GetComponentsInChildren<TMP_Text>().Any(t=>t.isTextOverflowing),"no clipped title/modal text");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
        }
        IEnumerator Flow()
        {
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","SmokeTitle"));Directory.CreateDirectory(output);
            yield return new WaitForSeconds(.8f);
            var session=app.Session;int energy=session.Energy;
            Require(app.Title!=null && app.Hub==null && app.Battle==null,"starts at title only");
            Require(app.Title.DisplayVersion=="Ver. 1.020","updated version");
            Require(TitleView.SocialUrl=="https://x.com/don_kng68","specified X destination");
            Capture("01-title");yield return null;
            Screen.SetResolution(480,800,FullScreenMode.Windowed);yield return new WaitForSeconds(.4f);
            Capture("02-title-480x800");yield return null;
            Screen.SetResolution(720,900,FullScreenMode.Windowed);yield return new WaitForSeconds(.4f);
            Capture("03-title-720x900");yield return null;
            for(int i=0;i<2;i++)
            {
                Click(Button("バックアップ連携"));yield return null;
                Require(app.Title.HasBackupPanel && !Button("TAP TO START").IsInteractable(),"backup blocks start");
                Require(!Button("Apple ID 連携").IsInteractable() && !Button("Google 連携").IsInteractable(),"link providers disabled");
                Click(Button("Apple ID 連携"));Click(Button("Google 連携"));Click(Button("TAP TO START"));
                app.Title.BeginStart();
                Require(app.Title.HasBackupPanel && !app.Title.IsStarting && app.Hub==null,"modal cannot accidentally start");
                Capture("04-backup-"+i);yield return null;
                Click(Button("閉じる"));yield return null;
                Require(!app.Title.HasBackupPanel && Button("TAP TO START").IsInteractable(),"close restores title");
            }
            Screen.SetResolution(540,960,FullScreenMode.Windowed);yield return new WaitForSeconds(.4f);
            Click(Button("バックアップ連携"));yield return null;Capture("05-backup");yield return null;
            Click(Button("閉じる"));yield return null;
            var start=Button("TAP TO START");Click(start);Click(start);
            Require(app.Title.IsStarting && app.Hub==null,"start transition precedes home");
            yield return new WaitForSeconds(.65f);
            Require(app.Title==null && app.Hub!=null && app.Hub.Page==HubPage.Home,"tap reaches home");
            Require(FindObjectsByType<TitleView>(FindObjectsSortMode.None).Length==0,"title disposed");
            app.StartGame();
            Require(FindObjectsByType<HubView>(FindObjectsSortMode.None).Length==1,"duplicate tap cannot create extra hub");
            Require(app.Session==session && session.Energy==energy,"title does not reset session or spend energy");
            Require(app.Hub.HasModal && !session.HasPlayerName,"name is required on first entry");
            Click(HubButton("冒険をはじめる"));yield return null;
            Require(app.Hub.HasModal && !session.HasPlayerName,"empty name is rejected");
            app.EnterBattle();Require(app.Battle==null,"cannot battle before entering name");
            Capture("06a-name-required");yield return null;
            app.Hub.GetComponentInChildren<TMP_InputField>().text="テスト冒険者";
            Click(HubButton("冒険をはじめる"));yield return null;
            Require(session.PlayerName=="テスト冒険者" && !app.Hub.HasModal,"name confirmation opens home");
            Capture("06-home");yield return null;
            app.EnterBattle();yield return new WaitForSeconds(.6f);
            Require(app.Battle!=null && !app.Battle.View.Feedback.DamageCounterVisible,"battle opens with hidden damage UI");
            app.ReturnHome();yield return new WaitForSeconds(.3f);
            Require(app.Title==null && app.Hub.Page==HubPage.Home,"battle returns to home");
            app.Hub.Navigate(HubPage.Settings);yield return null;
            Capture("07-settings");yield return null;
            Click(HubButton("プロフィール"));yield return null;
            Require(session.PlayerId==null && session.PlayerTitle==null,"undefined account ID/title remain null");
            Capture("08-profile");yield return null;
            Click(HubButton("Favorite_1"));yield return null;
            Click(HubButton("FavoriteSpirit_1"));yield return null;
            Require(session.Favorite(1)==1,"favorite selected from inventory");
            Click(HubButton("ひとこと変更"));yield return null;
            app.Hub.GetComponentInChildren<TMP_InputField>().text="チェインをつなごう！";
            Click(HubButton("保存"));yield return null;
            Require(session.Greeting=="チェインをつなごう！","greeting saved");
            Capture("09-profile-edited");yield return null;
            Click(HubButton("戻る"));yield return null;
            Click(HubButton("オプション"));yield return null;
            var sliders=app.Hub.GetComponentsInChildren<UnityEngine.UI.Slider>();
            Require(sliders.Length==3,"three volume channels");
            sliders.Single(s=>s.name=="Volume_BGM").value=37;
            Require(session.Volume(0)==37,"slider changes sound preference");
            Canvas.ForceUpdateCanvases();
            foreach(var slider in sliders)
            {
                float trackWidth=((RectTransform)slider.fillRect.parent).rect.width;
                Require(Mathf.Abs(slider.fillRect.rect.width-trackWidth*slider.normalizedValue)<.1f,"volume fill stays within its track and reflects the value");
            }
            Capture("10-options");yield return null;
            Click(HubButton("戻る"));yield return null;
            app.Hub.Navigate(HubPage.Home);yield return null;
            Click(HubButton("＞"));yield return null;
            Require(app.Hub.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==session.Config.catalog[1].displayName),"home cycles favorite");
            Capture("11-favorite-home");yield return null;
            app.Hub.Navigate(HubPage.Settings);yield return null;
            Click(HubButton("タイトルへ"));yield return null;
            Require(app.Title!=null && app.Hub==null && app.Session==session,"return to title preserves session");
            app.StartGame();yield return null;
            Require(!app.Hub.HasModal && session.Volume(0)==37,"name/options survive title round trip");
            app.Hub.Navigate(HubPage.Settings);yield return null;
            Click(HubButton("アカウント初期化"));yield return null;
            Click(HubButton("戻る"));yield return null;
            Require(app.Session==session && session.PlayerName=="テスト冒険者","reset cancellation preserves progress");
            Click(HubButton("アカウント初期化"));yield return null;
            Click(HubButton("決定"));yield return null;
            Require(app.Title!=null && app.Hub==null && app.Session!=session,"confirmed reset returns to title");
            Require(!app.Session.HasPlayerName && app.Session.Volume(0)==100 && app.Session.Favorite(1)==-1,"reset clears profile and options");
            Capture("12-reset-title");yield return null;
            Debug.Log("TITLE SMOKE PASS: initial title/version, 3 sizes, backup open/close/disabled providers, modal input, double tap, fade to home, one EventSystem, session preserved, battle return, no SE. X URL inspected; external browser not launched by test.");
            Debug.Log("PROFILE SMOKE PASS: mandatory name, blank rejection, name gate, owned favorites, home carousel, greeting, null ID/title, options sliders, title round trip, reset cancellation/confirmation.");
            Application.Quit(0);
        }
    }
}
