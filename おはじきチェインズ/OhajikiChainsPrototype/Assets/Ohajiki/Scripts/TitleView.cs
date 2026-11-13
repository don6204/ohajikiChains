using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Ohajiki
{
    public sealed class TitleView : MonoBehaviour
    {
        public const string SocialUrl="https://x.com/don_kng68";
        public event Action StartRequested;
        public bool HasBackupPanel=>backup!=null;
        public bool IsStarting { get; private set; }
        public string DisplayVersion=>"Ver. "+Application.version;
        Canvas canvas;
        CanvasGroup mainInteraction;
        HubWidgets ui;
        SpiritUiSkin skin;
        RectTransform screen,main,backup,logo;
        UnityEngine.UI.Image fade;
        TMP_Text prompt;
        readonly RectTransform[] characters=new RectTransform[3];
        readonly Vector2[] characterOrigins=new Vector2[3];
        readonly UnityEngine.UI.Image[] lights=new UnityEngine.UI.Image[18];
        readonly RectTransform[] orbs=new RectTransform[6];
        readonly Vector2[] orbOrigins=new Vector2[6];
        float clock,startAge;
        int lastWidth,lastHeight;

        public void Initialize(HubConfig config,TMP_FontAsset font)
        {
            skin=gameObject.AddComponent<SpiritUiSkin>();skin.Initialize();ui=new HubWidgets(font,skin);
            canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            new GameObject("TitleEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)).transform.SetParent(transform,false);
            screen=ui.Panel(transform,"TitleScreen",0,0,720,1280,new Color(.55f,.86f,1));
            screen.anchorMin=screen.anchorMax=screen.pivot=new Vector2(.5f,.5f);screen.anchoredPosition=Vector2.zero;
            skin.Backdrop(screen,false);
            main=ui.Rect(screen,"TitleContent",0,0,720,1280);
            mainInteraction=main.gameObject.AddComponent<CanvasGroup>();
            var start=ui.Button(main,"",0,0,720,1280,BeginStart,true,Color.clear);
            start.name="Button_TAP TO START";

            skin.Halo(main,new Vector2(360,625),520,new Color(1,.86f,.36f));
            var platform=skin.Image(main,"Adventure circle",skin.Ring,360,424,580,150,new Color(1,.93f,.57f,.85f),true);
            platform.rectTransform.localRotation=Quaternion.Euler(0,0,-4);
            // Reuse the authored characters without changing their source images.
            Element[] elements={Element.Wood,Element.Water,Element.Fire};
            Vector2[] positions={new Vector2(10,448),new Vector2(430,445),new Vector2(170,407)};
            Vector2[] sizes={new Vector2(280,390),new Vector2(280,390),new Vector2(380,482)};
            for(int i=0;i<characters.Length;i++)
            {
                SpiritDefinition spirit=Array.Find(config.catalog,s=>s!=null && s.element==elements[i]);
                characters[i]=ui.Rect(main,"Title spirit "+elements[i],positions[i].x,positions[i].y,sizes[i].x,sizes[i].y);
                characterOrigins[i]=positions[i];
                if(spirit!=null)ui.Portrait(characters[i],spirit.portrait,0,0,sizes[i].x,sizes[i].y);
            }
            for(int i=0;i<orbs.Length;i++)
            {
                float angle=(i*36+2)*Mathf.Deg2Rad;
                var pos=new Vector2(360+Mathf.Cos(angle)*277,470-Mathf.Sin(angle)*116);
                orbOrigins[i]=pos;
                orbs[i]=skin.Image(main,"Floating spirit orb",skin.Ball,pos.x,pos.y,i%2==0?54:40,i%2==0?54:40,
                    Color.Lerp(HubWidgets.Elements[i%3],Color.white,.28f),true).rectTransform;
            }
            for(int i=0;i<lights.Length;i++)
                lights[i]=skin.Image(main,"Adventure sparkle",i%3==0?skin.Star:skin.Glow,0,0,i%3==0?19:12,i%3==0?19:12,Color.white,true);

            logo=ui.Rect(main,"Title logo",360,1000,640,220);logo.pivot=new Vector2(.5f,.5f);
            var first=ui.Label(logo,"おはじき",0,80,640,160,70,new Color(1,.63f,.15f),true);
            var second=ui.Label(logo,"チェインズ",0,-23,640,180,88,new Color(.24f,.86f,.96f),true);
            skin.Text(first,true);skin.Text(second,true);
            first.enableVertexGradient=second.enableVertexGradient=true;
            first.color=second.color=Color.white;
            first.colorGradient=new VertexGradient(new Color(1,.98f,.70f),new Color(1,.98f,.70f),new Color(1,.56f,.11f),new Color(1,.56f,.11f));
            second.colorGradient=new VertexGradient(new Color(.88f,1,1),new Color(.88f,1,1),new Color(.08f,.66f,.82f),new Color(.08f,.66f,.82f));
            var tagline=ui.Label(main,"はじいて、つないで、大冒険！",70,866,580,45,25,Color.white,true);skin.Text(tagline,true);

            var startGlow=skin.Image(main,"Start glow",skin.Glow,360,264,620,230,new Color(1,.98f,.8f,.95f),true);
            prompt=ui.Label(main,"TAP TO START",80,238,560,64,38,SpiritUiSkin.Ink,true);
            ui.Label(main,"タップして冒険へ",80,204,560,37,22,SpiritUiSkin.Muted,true);
            skin.Image(main,"Start jewel left",skin.Shard,97,270,12,23,SpiritUiSkin.Gold,true);
            skin.Image(main,"Start jewel right",skin.Shard,623,270,12,23,SpiritUiSkin.Gold,true);
            ui.Label(main,DisplayVersion,32,1222,260,34,19,SpiritUiSkin.Ink);
            ui.Button(main,"バックアップ連携",32,1146,245,63,OpenBackup);
            ui.Button(main,"X",624,1146,64,63,()=>Application.OpenURL(SocialUrl));
            ui.Label(main,"© 2026 おはじきチェインズ",35,43,650,30,16,SpiritUiSkin.Ink,true);
            fade=skin.Image(screen,"Start fade",null,0,0,720,1280,new Color(1,.98f,.88f,0));
            Resize();
        }
        public void OpenBackup()
        {
            if(HasBackupPanel || IsStarting)return;
            mainInteraction.interactable=false;mainInteraction.blocksRaycasts=false;
            backup=ui.Panel(screen,"BackupLink",0,0,720,1280,new Color(.12f,.18f,.20f,.68f),true);
            var panel=ui.Panel(backup,"Backup card",54,377,612,526,SpiritUiSkin.Paper,true);
            ui.Label(panel,"バックアップ連携",32,414,548,70,35,SpiritUiSkin.Ink,true);
            ui.Label(panel,"連携機能は準備中です",32,362,548,42,22,SpiritUiSkin.Muted,true);
            ui.Button(panel,"Apple ID 連携",50,256,512,76,null,false);
            ui.Button(panel,"Google 連携",50,152,512,76,null,false);
            ui.Button(panel,"閉じる",155,41,302,72,CloseBackup,true,SpiritUiSkin.Accent);
            EventSystem.current?.SetSelectedGameObject(null);
        }
        public void CloseBackup()
        {
            if(!HasBackupPanel)return;
            backup.gameObject.SetActive(false);Destroy(backup.gameObject);backup=null;
            mainInteraction.interactable=true;mainInteraction.blocksRaycasts=true;
        }
        public void BeginStart()
        {
            if(IsStarting || HasBackupPanel)return;
            IsStarting=true;startAge=0;prompt.text="冒険へ出発！";
            mainInteraction.interactable=false;mainInteraction.blocksRaycasts=false;
        }
        void Resize()
        {
            if(lastWidth==Screen.width && lastHeight==Screen.height)return;
            lastWidth=Screen.width;lastHeight=Screen.height;
            canvas.scaleFactor=Mathf.Min(Screen.width/720f,Screen.height/1280f);
        }
        void Update()
        {
            if(canvas==null)return;
            Resize();clock+=Time.unscaledDeltaTime;
            logo.anchoredPosition=new Vector2(360,1000+Mathf.Sin(clock*MasterConstants.GetFloat("TitleView.logoFloatAngularRate",.9f))*MasterConstants.GetFloat("TitleView.logoFloatAmplitude",4));
            for(int i=0;i<characters.Length;i++)characters[i].anchoredPosition=characterOrigins[i]+Vector2.up*Mathf.Sin(clock*1.1f+i*1.7f)*5;
            for(int i=0;i<orbs.Length;i++)orbs[i].anchoredPosition=orbOrigins[i]+Vector2.up*Mathf.Sin(clock*1.6f+i)*9;
            for(int i=0;i<lights.Length;i++)
            {
                float p=Mathf.Repeat(clock*.045f+i*.618f,1);
                lights[i].rectTransform.anchoredPosition=new Vector2(50+Mathf.Repeat(i*.381f,1)*620+Mathf.Sin(clock+i)*10,340+p*810);
                lights[i].color=new Color(1,1,.8f,Mathf.Sin(p*Mathf.PI)*(.35f+.35f*Mathf.Sin(clock*2+i)));
            }
            float minAlpha=MasterConstants.GetFloat("TitleView.promptMinAlpha",.72f);
            prompt.alpha=minAlpha+(1-minAlpha)*(.5f+.5f*Mathf.Sin(clock*MasterConstants.GetFloat("TitleView.promptPulseAngularRate",2.7f)));
            if(!IsStarting)return;
            startAge+=Time.unscaledDeltaTime;
            float fadeSeconds=MasterConstants.GetFloat("TitleView.startFadeSeconds",.45f);
            fade.color=new Color(1,.98f,.88f,Mathf.SmoothStep(0,1,startAge/fadeSeconds));
            if(startAge>=fadeSeconds)StartRequested?.Invoke();
        }
    }
}
