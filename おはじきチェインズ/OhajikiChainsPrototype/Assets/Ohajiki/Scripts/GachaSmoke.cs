using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
namespace Ohajiki
{
    // Real player integration check, attached only with --gacha-smoke-test.
    public sealed class GachaSmoke : MonoBehaviour
    {
        PrototypeApp app;string directory;bool recording;int frame;float nextFrame;
        public void Run(PrototypeApp owner){app=owner;StartCoroutine(Guard(Flow()));}
        IEnumerator Guard(IEnumerator flow)
        {
            while(true)
            {
                object next;
                try{if(!flow.MoveNext())yield break;next=flow.Current;}
                catch(Exception e){Debug.LogError("GACHA SMOKE FAIL: "+e);Application.Quit(25);yield break;}
                yield return next;
            }
        }
        void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        void Click(string title)
        {
            var button=app.Hub.GetComponentsInChildren<UnityEngine.UI.Button>().LastOrDefault(b=>b.IsInteractable() &&
                (b.name=="Button_"+title || b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==title)));
            Require(button!=null,"Button missing: "+title);button.onClick.Invoke();
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();UiSmokeChecks.Validate(app.Hub.gameObject);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));yield return null;
        }
        void Update()
        {
            if(recording && Time.unscaledTime>=nextFrame)
            {nextFrame=Time.unscaledTime+.05f;ScreenCapture.CaptureScreenshot(Path.Combine(directory,"Frames",(frame++).ToString("D3")+".png"));}
        }
        void OpenBanner(int index){app.Hub.Navigate(HubPage.Gacha);Click(app.Session.Config.banners[index].title);}
        int Copies()=>Enumerable.Range(0,app.Session.Config.catalog.Length).Sum(app.Session.Copies);
        IEnumerator Flow()
        {
            directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","SmokeGacha"));Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.Combine(directory,"Frames"));
            yield return new WaitForSeconds(.8f);
            var session=app.Session;var hub=app.Hub;
            OpenBanner(0);hub.RequestGacha(10);Require(hub.GachaMovie==null && session.Gems==0,"insufficient gems no movie");
            Require(session.Shop.Confirm(session.Shop.Prepare("gems_180",out _),out _),"fund through simulated purchase");hub.Refresh();
            int first=hub.FeaturedSpiritId;yield return new WaitForSeconds(5.3f);
            Require(hub.FeaturedSpiritId!=first,"five second featured rotation");yield return Capture("01-banner");
            int gems=session.Gems,copies=Copies();
            hub.RequestGacha(1);yield return Capture("02-confirm");Click("戻る");
            Require(session.Gems==gems && Copies()==copies && !session.FirstSingleUsed(0),"cancel preserves currency and discount");
            hub.RequestGacha(1);
            var confirmation=hub.GetComponentsInChildren<UnityEngine.UI.Button>().Last(b=>b.IsInteractable() && b.name=="Button_決定");
            confirmation.onClick.Invoke();confirmation.onClick.Invoke();recording=true;
            var movie=hub.GachaMovie;Require(movie!=null && session.Gems==gems-1 && Copies()==copies+1,"single committed once");
            Vector3 cameraBefore=movie.Stage.MovieCamera.transform.position;
            yield return new WaitForSeconds(1.3f);yield return Capture("03-3d-approach");
            Require(movie.Stage.Target.IsCreated() && movie.Stage.MovieCamera.transform.position!=cameraBefore,"3D camera renders and moves");
            float deadline=Time.realtimeSinceStartup+12;
            while(movie.Phase!=SummonPhase.Drop && Time.realtimeSinceStartup<deadline)yield return null;
            Require(movie.Phase==SummonPhase.Drop,"drop phase");yield return new WaitForSeconds(.7f);yield return Capture("04-3d-drop");
            while(movie.Phase!=SummonPhase.Release && Time.realtimeSinceStartup<deadline)yield return null;
            Require(movie.Phase==SummonPhase.Release,"release phase");yield return new WaitForSeconds(.25f);yield return Capture("05-release");
            while(movie.Phase!=SummonPhase.Reveal && Time.realtimeSinceStartup<deadline)yield return null;
            Require(movie.Phase==SummonPhase.Reveal,"single reveal");yield return new WaitForSeconds(.7f);yield return Capture("06-reveal");
            movie.Skip();movie.Skip();Require(session.Gems==gems-1 && Copies()==copies+1,"skip cannot grant twice");
            recording=false;movie.Next();yield return null;yield return null;
            Require(hub.Page==HubPage.Gacha && !hub.HasModal,"single returns gacha home");
            Require(FindObjectsByType<GachaStage3D>(FindObjectsSortMode.None).Length==0,"3D stage teardown");
            OpenBanner(1);Require(session.GachaCost(1,1)==1 && session.GachaCost(0,1)==5,"first discount is per banner");
            gems=session.Gems;copies=Copies();hub.RequestGacha(10);Click("決定");movie=hub.GachaMovie;
            Require(session.Gems==gems-49 && Copies()==copies+10 && movie.Receipt.Bonus==1,"ten paid and bonus committed once");
            for(int index=0;index<10;index++)
            {
                deadline=Time.realtimeSinceStartup+10;
                while(movie.Phase!=SummonPhase.Reveal && Time.realtimeSinceStartup<deadline)yield return null;
                Require(movie.Phase==SummonPhase.Reveal && movie.RevealIndex==index,"ten individual reveal "+index);
                yield return new WaitForSeconds(.5f);movie.Next();
            }
            Require(movie.Phase==SummonPhase.Summary,"ten summary after all reveals");yield return Capture("07-ten-results");
            Require(session.Gems==gems-49 && Copies()==copies+10,"next never rerolls");Click("つぎへ");yield return null;
            OpenBanner(2);gems=session.Gems;copies=Copies();hub.RequestGacha(10);Click("決定");movie=hub.GachaMovie;
            movie.Skip();movie.Skip();Require(movie.Phase==SummonPhase.Summary,"ten skip jumps summary");
            Require(session.Gems==gems-49 && Copies()==copies+10,"ten skip unchanged receipt");yield return Capture("08-ten-skip");Click("つぎへ");yield return null;
            OpenBanner(0);gems=session.Gems;hub.RequestGacha(1);Click("決定");movie=hub.GachaMovie;
            movie.Skip();Require(movie.Phase==SummonPhase.Reveal,"single skip reaches stage four");yield return new WaitForSeconds(.5f);movie.Next();yield return null;
            OpenBanner(0);gems=session.Gems;hub.RequestGacha(1);Click("決定");
            app.EnterBattle();yield return null;yield return null;yield return null;
            Require(session.Gems==gems-5,"interruption retains one committed draw");
            Require(FindObjectsByType<GachaStage3D>(FindObjectsSortMode.None).Length==0,"interrupt destroys stage");
            app.ReturnHome();yield return null;yield return null;
            Require(FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length==0,"no SE");
            Require(FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c=>c.targetTexture!=null)==0,"no render camera leak");
            Debug.Log("GACHA SMOKE PASS: affordability, confirmation/cancel, per-banner first discount, featured carousel, real 3D camera, all four stages, ten individual reveals, bonus, both skip modes, no duplicate rewards, navigation and interruption cleanup, no SE.");
            Application.Quit(0);
        }
    }
}
