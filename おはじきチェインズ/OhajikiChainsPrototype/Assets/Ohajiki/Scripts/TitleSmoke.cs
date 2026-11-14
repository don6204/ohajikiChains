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
            Require(app.Title.DisplayVersion=="Ver. 1.010","updated version");
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
            Capture("06-home");yield return null;
            app.EnterBattle();yield return new WaitForSeconds(.6f);
            Require(app.Battle!=null && !app.Battle.View.Feedback.HitCounterVisible,"battle opens with hidden damage UI");
            app.ReturnHome();yield return new WaitForSeconds(.3f);
            Require(app.Title==null && app.Hub.Page==HubPage.Home,"battle returns to home");
            Debug.Log("TITLE SMOKE PASS: initial title/version, 3 sizes, backup open/close/disabled providers, modal input, double tap, fade to home, one EventSystem, session preserved, battle return, no SE. X URL inspected; external browser not launched by test.");
            Application.Quit(0);
        }
    }
}
