using System;
using TMPro;
using UnityEngine;
namespace Ohajiki
{
    public enum SummonPhase { Approach, Drop, Release, Reveal, Summary }
    public sealed class GachaSequence : MonoBehaviour
    {
        public SummonPhase Phase { get; private set; }
        public int RevealIndex { get; private set; }
        public GachaReceipt Receipt { get; private set; }
        public GachaStage3D Stage { get; private set; }
        HubWidgets ui;SpiritUiSkin skin;PlayerSession session;Action finished;
        RectTransform root,revealLayer,art;
        UnityEngine.UI.Image flash,halo;
        UnityEngine.UI.Image[] rays;
        UnityEngine.UI.Button next,skip;
        TMP_Text stageTitle,stageSubtitle,rarity;
        CanvasGroup revealGroup;
        float age,clock;
        bool complete;
        public void Initialize(HubWidgets ui,SpiritUiSkin skin,PlayerSession session,GachaReceipt receipt,Action finished)
        {
            this.ui=ui;this.skin=skin;this.session=session;Receipt=receipt;this.finished=finished;root=(RectTransform)transform;
            Stage=gameObject.AddComponent<GachaStage3D>();Stage.Initialize(root,ui);
            revealLayer=ui.Rect(root,"Summon reveal",0,0,720,1280);
            revealGroup=revealLayer.gameObject.AddComponent<CanvasGroup>();
            halo=skin.Image(root,"Release radiance",skin.Glow,360,640,900,900,Color.clear,true);
            rays=new UnityEngine.UI.Image[24];
            for(int i=0;i<rays.Length;i++)rays[i]=skin.Image(root,"Release spark",i%2==0?skin.Star:skin.Shard,360,640,20,80,Color.clear,true);
            flash=skin.Image(root,"Warm release flash",null,0,0,720,1280,Color.clear);
            stageTitle=ui.Label(root,"精霊の遺跡",36,1060,648,88,43,Color.white,true);skin.Text(stageTitle,true);
            stageSubtitle=ui.Label(root,"SPIRIT SUMMON",40,1010,640,55,22,new Color(1,.92f,.5f),true);skin.Text(stageSubtitle,true);
            skip=ui.Button(root,"スキップ",530,1185,156,58,Skip,true,SpiritUiSkin.Paper);
            Begin(SummonPhase.Approach);
        }
        SpiritDefinition Current=>session.Config.catalog[Receipt.SpiritIds[RevealIndex]];
        void Begin(SummonPhase phase)
        {
            Phase=phase;age=0;
            flash.color=halo.color=Color.clear;foreach(var ray in rays)ray.color=Color.clear;
            bool film=phase==SummonPhase.Approach || phase==SummonPhase.Drop || phase==SummonPhase.Release;
            Stage.Show(film);revealLayer.gameObject.SetActive(!film);
            stageTitle.gameObject.SetActive(film);stageSubtitle.gameObject.SetActive(film);
            skip.gameObject.SetActive(phase!=SummonPhase.Summary);
            if(film)
            {
                Stage.SetSpirit(Current);
                stageTitle.text=phase==SummonPhase.Approach?"精霊の遺跡":phase==SummonPhase.Drop?"想いが、宿る。":"解き放て！";
                stageSubtitle.text=Receipt.SpiritIds.Count==10?$"SPIRIT SUMMON  {RevealIndex+1} / 10":"SPIRIT SUMMON";
            }
            else if(phase==SummonPhase.Reveal)BuildReveal();
            else BuildSummary();
        }
        void ClearReveal(){foreach(Transform child in revealLayer){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
        void BuildReveal()
        {
            ClearReveal();skin.Backdrop(revealLayer,false);
            var spirit=Current;
            skin.Halo(revealLayer,new Vector2(360,734),580,spirit.rarity>=4?SpiritUiSkin.Gold:HubWidgets.Elements[(int)spirit.element]);
            ui.Panel(revealLayer,"Result name plate",40,257,640,237,new Color(1,.97f,.83f,.96f));
            var portrait=skin.Image(revealLayer,"Full spirit reveal",spirit.portrait,360,758,485,565,Color.white,true);
            portrait.preserveAspect=true;art=portrait.rectTransform;
            var newText=ui.Label(revealLayer,Receipt.NewSpirits[RevealIndex]?"NEW SPIRIT!":"SPIRIT GET!",40,1080,640,84,44,SpiritUiSkin.Gold,true);skin.Text(newText,true);
            rarity=ui.Label(revealLayer,new string('★',spirit.rarity),42,1005,636,72,42,SpiritUiSkin.Gold,true);skin.Text(rarity,true);
            ui.Label(revealLayer,spirit.displayName,62,386,596,90,40,SpiritUiSkin.Ink,true);
            ui.Label(revealLayer,"を手に入れた",62,332,596,54,25,SpiritUiSkin.Muted,true);
            ui.Label(revealLayer,$"{new[]{"火","水","木"}[(int)spirit.element]}属性　/　★{spirit.rarity}"+
                (Receipt.SpiritIds.Count==10?$"　 {RevealIndex+1} / 10":""),62,275,596,50,24,SpiritUiSkin.Ink,true);
            next=ui.Button(revealLayer,"つぎへ",130,135,460,86,Next,true,SpiritUiSkin.Accent);
            next.interactable=false;revealGroup.alpha=0;
            ui.Label(revealLayer,Receipt.SpiritIds.Count==10?"1体ずつ確認 / スキップで10体の結果へ":"宝石消費・獲得は反映済み",45,62,630,50,18,SpiritUiSkin.Muted,true);
        }
        void BuildSummary()
        {
            ClearReveal();skin.Backdrop(revealLayer,false);revealGroup.alpha=1;
            ui.Panel(revealLayer,"Ten summon results",28,243,664,807,SpiritUiSkin.Panel);
            ui.Label(revealLayer,"ガチャ結果",45,1090,630,90,44,SpiritUiSkin.Ink,true);
            ui.Label(revealLayer,$"10体のスピリターを獲得！",45,1030,630,65,25,SpiritUiSkin.Muted,true);
            for(int i=0;i<Receipt.SpiritIds.Count;i++)
            {
                int id=Receipt.SpiritIds[i];var spirit=session.Config.catalog[id];
                float x=44+(i%5)*127,y=665-(i/5)*330;
                var cell=ui.Panel(revealLayer,"Result "+i,x,y,122,300,Color.Lerp(SpiritUiSkin.Paper,HubWidgets.Elements[(int)spirit.element],.10f));
                var ball=ui.Rect(cell,"Spirit ball",9,149,104,104).gameObject.AddComponent<SpiritBallVisual>();ball.Initialize(skin);ball.Show(spirit);
                ui.Label(cell,new string('★',spirit.rarity),2,109,118,40,18,SpiritUiSkin.Ink,true);
                ui.Label(cell,spirit.displayName,8,31,106,81,19,SpiritUiSkin.Ink,true);
                if(Receipt.NewSpirits[i])ui.Label(cell,"NEW",8,254,106,35,19,new Color(.84f,.25f,.045f),true);
            }
            ui.Label(revealLayer,$"宝石 −{Receipt.Cost}　おまけ ＋{Receipt.Bonus}\n現在の宝石 {session.Gems}個",45,222,630,95,24,SpiritUiSkin.Ink,true);
            next=ui.Button(revealLayer,"つぎへ",130,110,460,86,Finish,true,SpiritUiSkin.Accent);
        }
        public void Skip()
        {
            if(complete || Phase==SummonPhase.Summary)return;
            if(Receipt.SpiritIds.Count==10)Begin(SummonPhase.Summary);
            else if(Phase!=SummonPhase.Reveal)Begin(SummonPhase.Reveal);
        }
        public void Next()
        {
            if(complete || Phase!=SummonPhase.Reveal || age<.45f)return;
            if(Receipt.SpiritIds.Count==1){Finish();return;}
            if(RevealIndex+1==Receipt.SpiritIds.Count){Begin(SummonPhase.Summary);return;}
            RevealIndex++;Begin(SummonPhase.Drop);
        }
        void Finish(){if(complete)return;complete=true;finished?.Invoke();}
        void Update()
        {
            if(Receipt==null || complete)return;
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);age+=dt;clock+=dt;
            float duration=Phase==SummonPhase.Approach?3.5f:Phase==SummonPhase.Drop?1.25f:.7f;
            if(Phase==SummonPhase.Approach || Phase==SummonPhase.Drop || Phase==SummonPhase.Release)
            {
                Stage.Film((int)Phase,Mathf.Clamp01(age/duration),clock);
                if(age>=duration)Begin((SummonPhase)((int)Phase+1));
            }
            if(Phase==SummonPhase.Reveal)
            {
                revealGroup.alpha=Mathf.Clamp01(age/.22f);next.interactable=age>=.45f;
                art.localScale=Vector3.one*(1+.23f*Mathf.Exp(-age*6));
                art.anchoredPosition=new Vector2(360,758+Mathf.Sin(clock*1.5f)*5);
                rarity.maxVisibleCharacters=Mathf.Clamp(1+Mathf.FloorToInt(age/.14f),1,Current.rarity);
            }
            float burst=Phase==SummonPhase.Release?Mathf.Sin(Mathf.Clamp01(age/.7f)*Mathf.PI):Phase==SummonPhase.Reveal?Mathf.Max(0,1-age/.8f):0;
            flash.color=new Color(1,.96f,.7f,Phase==SummonPhase.Release?burst*.65f:0);
            halo.color=new Color(1,.86f,.3f,burst*.75f);
            for(int i=0;i<rays.Length;i++)
            {
                float a=i*Mathf.PI*2/rays.Length+clock*.12f;
                rays[i].rectTransform.anchoredPosition=new Vector2(360+Mathf.Cos(a)*(180+(1-burst)*260),680+Mathf.Sin(a)*(220+(1-burst)*330));
                rays[i].rectTransform.localRotation=Quaternion.Euler(0,0,a*Mathf.Rad2Deg-90);
                rays[i].rectTransform.sizeDelta=new Vector2(i%2==0?26:12,i%2==0?26:130)*(.6f+burst*.6f);
                rays[i].color=new Color(1,.86f,.25f,burst);
            }
        }
    }
}
