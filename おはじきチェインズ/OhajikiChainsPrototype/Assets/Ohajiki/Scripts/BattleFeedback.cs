using TMPro;
using UnityEngine;
namespace Ohajiki
{
    // Presentation only. Hit counts and damage always come from the battle model.
    public sealed class BattleFeedback : MonoBehaviour
    {
        sealed class Burst
        {
            public RectTransform root;
            public UnityEngine.UI.Image ring,echo,glow,blast;
            public UnityEngine.UI.Image[] sparks,motes;
            public TMP_Text number;
            public Vector2 origin;
            public Color color;
            public float age,scale,rotation;
            public bool active;
        }
        HubWidgets ui;
        SpiritUiSkin skin;
        BattleTuning tuning;
        RectTransform board,bank;
        UnityEngine.UI.Image bankBurst,impactRing,impactBurst,beam,chargeOrb;
        UnityEngine.UI.Image[] impactRays;
        UnityEngine.UI.Image flightCore,flightGlow;
        UnityEngine.UI.Image[] flightTail;
        TMP_Text flightNumber;
        TMP_Text number,celebration,allClear;
        Burst[] bursts;
        readonly Vector2 bankCenter=new Vector2(530,970);
        readonly Vector2 numberCenter=new Vector2(190,130);
        float NumberTilt=>tuning.damageNumberTilt;
        float DamageRestScale=>tuning.damageBaseScale;
        float DamagePulseSeconds=>tuning.damagePulseSeconds;
        readonly Color gold=new Color(1,.82f,.12f);
        readonly Color[] colors={new Color(1,.27f,.055f),new Color(.08f,.68f,1),new Color(.28f,.90f,.17f),new Color(.70f,.3f,1)};
        Vector2 shotPosition,chargeStart,impactCenter;
        int burstCursor,lastTarget,lastContacts,lastLevel=1,lastTier;
        float impactAge=10,celebrateAge=10,lockAge=10,allClearAge,damagePulseAge=1;
        bool paused,showBeam,charging;
        public int DamageTarget=>lastTarget;
        public int ContactCount=>lastContacts;
        public int DisplayedDamage=>lastTarget;
        public int DamageColorFullAt=>Mathf.Max(1,tuning.damageColorFullAt);
        // Planner-approved steps: 500 damage adds 10%, capped at +50%.
        public float DamageSizeMultiplier=>Mathf.Min(tuning.damageSizeMaximum,1+(lastTarget/Mathf.Max(1,tuning.damageSizeStep))*tuning.damageSizeIncrement);
        public bool DamageVisualsVisible=>flightNumber.gameObject.activeInHierarchy || impactBurst.color.a>0 || impactRing.color.a>0;
        public Vector3 BankScale=>bank.localScale;
        public bool DamageCounterVisible=>bank.gameObject.activeInHierarchy;
        public Color DamageCounterColor=>bankBurst.color;
        public Color DamageNumberColor=>number.color;
        public Vector3 DamageNumberScale=>number.rectTransform.localScale;
        public Vector2 BankPosition=>bank.anchoredPosition;
        public Quaternion BankRotation=>bank.localRotation;
        public Vector2 DamageNumberPosition=>number.rectTransform.anchoredPosition;
        public bool AllClearVisible=>allClear.gameObject.activeInHierarchy;
        public Vector2 AllClearPosition=>allClear.rectTransform.anchoredPosition;
        public void SetDamageCounterVisible(bool visible)=>bank.gameObject.SetActive(visible);
        public int ActiveBursts {get {int n=0;foreach(var b in bursts)if(b.active)n++;return n;}}
        public int PoolCapacity=>bursts.Length;
        public void Initialize(RectTransform board,TMP_FontAsset font,SpiritUiSkin skin,BattleTuning config)
        {
            this.board=board;this.skin=skin;tuning=config;ui=new HubWidgets(font);
            bursts=new Burst[Mathf.Clamp(tuning.contactEffectPoolSize,4,32)];
            for(int i=0;i<bursts.Length;i++)
            {
                var b=new Burst{root=ui.Rect(transform,"Contact burst",0,0,1,1)};
                b.glow=Graphic(b.root,"Element glow",skin.Glow,Vector2.zero,180);
                b.blast=Graphic(b.root,"Contact starburst",skin.Burst,Vector2.zero,95);
                b.ring=Graphic(b.root,"Shock ring",skin.Ring,Vector2.zero,40);
                b.echo=Graphic(b.root,"Second shock ring",skin.Ring,Vector2.zero,40);
                b.sparks=new UnityEngine.UI.Image[12];b.motes=new UnityEngine.UI.Image[4];
                for(int j=0;j<b.sparks.Length;j++)b.sparks[j]=Graphic(b.root,"Spark",j%3==0?skin.Star:skin.Shard,Vector2.zero,14);
                for(int j=0;j<b.motes.Length;j++)b.motes[j]=Graphic(b.root,"Flying energy",skin.Star,Vector2.zero,25);
                b.number=Label(b.root,"",Vector2.zero,new Vector2(180,64),38,Color.white);
                b.root.gameObject.SetActive(false);bursts[i]=b;
            }
            celebration=Label(transform,"",new Vector2(360,879),new Vector2(600,80),45,gold);
            // Reference video: tilted digits pop and shake; the burst center stays fixed.
            bank=ui.Rect(transform,"Damage counter",bankCenter.x,bankCenter.y,380,260);bank.pivot=new Vector2(.5f,.5f);
            bankBurst=Graphic(bank,"Damage burst",Resources.Load<Sprite>("UI/HitCounterBurst")??skin.DamageBurst,numberCenter,1);
            bankBurst.rectTransform.sizeDelta=new Vector2(380,260);
            number=ui.Label(bank,"0",numberCenter.x,numberCenter.y,250,120,96,gold,true);
            number.rectTransform.pivot=new Vector2(.5f,.5f);
            number.fontStyle=FontStyles.Normal;
            number.textWrappingMode=TextWrappingModes.NoWrap;
            number.enableAutoSizing=true;number.fontSizeMin=28;number.fontSizeMax=96;
            number.enableVertexGradient=false;
            skin.Text(number,true);
            number.outlineColor=new Color(.025f,.07f,.08f,1);number.outlineWidth=.16f;
            beam=Graphic(transform,"Damage streak",skin.Glow,bankCenter,1);beam.rectTransform.pivot=new Vector2(0,.5f);
            impactBurst=Graphic(transform,"Damage impact burst",skin.Burst,Vector2.zero,1);
            impactRing=Graphic(transform,"Target shockwave",skin.Ring,Vector2.zero,100);
            impactRays=new UnityEngine.UI.Image[14];
            for(int i=0;i<impactRays.Length;i++)impactRays[i]=Graphic(transform,"Impact ray",skin.Shard,Vector2.zero,1);
            chargeOrb=Graphic(transform,"Final charge",skin.Star,Vector2.zero,60);
            flightTail=new UnityEngine.UI.Image[10];
            for(int i=0;i<flightTail.Length;i++)flightTail[i]=Graphic(transform,"Damage comet tail",skin.Glow,Vector2.zero,100);
            flightGlow=Graphic(transform,"Damage comet glow",skin.Glow,Vector2.zero,180);
            flightCore=Graphic(transform,"Damage comet core",skin.Star,Vector2.zero,90);
            flightNumber=Label(transform,"",Vector2.zero,new Vector2(230,110),64,Color.white);
            flightNumber.enableAutoSizing=true;flightNumber.fontSizeMin=30;flightNumber.fontSizeMax=64;
            var allClearViewport=ui.Rect(transform,"AllClearViewport",0,board.anchoredPosition.y+ShotSimulation.Height*.5f-100,720,200);
            allClearViewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            allClear=Label(allClearViewport,"全消し",new Vector2(930,100),new Vector2(420,150),96,gold);
            allClear.gameObject.name="AllClearText";
            allClear.textWrappingMode=TextWrappingModes.NoWrap;
            allClear.raycastTarget=false;
            ResetFeedback();
        }
        TMP_Text Label(Transform parent,string value,Vector2 center,Vector2 size,int fontSize,Color color)
        {
            var label=ui.Label(parent,value,center.x,center.y,size.x,size.y,fontSize,color,true);
            label.rectTransform.pivot=new Vector2(.5f,.5f);skin.Text(label,true);return label;
        }
        UnityEngine.UI.Image Graphic(Transform parent,string name,Sprite sprite,Vector2 center,float size)
        {return skin.Image(parent,name,sprite,center.x,center.y,size,size,Color.clear,true);}
        static Color Alpha(Color color,float alpha){color.a=Mathf.Clamp01(alpha);return color;}
        public void ResetFeedback()
        {
            foreach(var b in bursts){b.active=false;b.root.gameObject.SetActive(false);}
            foreach(var ray in impactRays)ray.color=Color.clear;
            paused=showBeam=charging=false;
            lastTarget=lastContacts=lastTier=0;lastLevel=1;damagePulseAge=DamagePulseSeconds;
            impactAge=MasterConstants.GetFloat("BattleFeedback.impactVisualSeconds",.55f);
            celebrateAge=MasterConstants.GetFloat("BattleFeedback.celebrationSeconds",.72f);lockAge=10;
            allClearAge=0;allClear.gameObject.SetActive(false);
            bank.anchoredPosition=bankCenter;bank.localRotation=Quaternion.identity;
            number.text="0";number.rectTransform.localScale=Vector3.one;
            number.rectTransform.anchoredPosition=numberCenter;number.rectTransform.localRotation=Quaternion.Euler(0,0,NumberTilt);
            celebration.text="";
            impactRing.color=impactBurst.color=beam.color=chargeOrb.color=Color.clear;
            AnimateDamageCounter();
            HideFlight();
            SetDamageCounterVisible(false);
        }
        public void BeginShot(bool isEnemy,Element element)
        {ResetFeedback();}
        public void ShowAllClear()
        {
            allClearAge=0;
            allClear.rectTransform.anchoredPosition=new Vector2(930,100);
            allClear.gameObject.SetActive(true);
        }
        public void MoveShot(Vector2 position)=>shotPosition=board.anchoredPosition+position;
        public void SetPaused(bool value)=>paused=value;
        void AnimateDamageCounter()
        {
            bankBurst.color=Color.white;
            number.color=Color.Lerp(new Color(1,.88f,.08f),new Color(1,.08f,.035f),
                Mathf.Clamp01((float)lastTarget/DamageColorFullAt));
            float pulse=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(damagePulseAge/DamagePulseSeconds));
            float scale=DamageRestScale*DamageSizeMultiplier;
            bank.localScale=new Vector3(scale,scale,1);
            number.rectTransform.localScale=Vector3.one*(1+.24f*pulse);
            number.rectTransform.anchoredPosition=numberCenter+new Vector2(
                Mathf.Sin(damagePulseAge*105)*3,Mathf.Sin(damagePulseAge*81)*2)*pulse;
            number.rectTransform.localRotation=Quaternion.Euler(0,0,NumberTilt);
        }
        public void Contact(Vector2 position,Element element,bool same,int contacts,int matched,int total)
        {
            int level=BattleRules.EffectLevel(matched,tuning),delta=Mathf.Max(0,total-lastTarget),tier=0;
            if(tuning.contactCelebrationThresholds!=null)foreach(int threshold in tuning.contactCelebrationThresholds)if(contacts>=threshold)tier++;
            bool levelUp=level>lastLevel;
            if(tier>lastTier || levelUp)
            {
                celebration.text=tier>lastTier?$"{contacts} CHAIN!":$"SKILL Lv{level} UP!";
                celebrateAge=0;celebration.color=tier>lastTier?gold:new Color(.55f,1,.64f);
            }
            lastTarget=total;lastContacts=contacts;lastLevel=level;lastTier=tier;
            number.text=total.ToString("N0");
            if(delta>0)damagePulseAge=0;
            AnimateDamageCounter();
            var b=bursts[burstCursor++%bursts.Length];b.age=0;b.rotation=contacts*29;b.active=true;
            b.origin=board.anchoredPosition+position;b.color=colors[(int)element];
            b.scale=(same?MasterConstants.GetFloat("BattleFeedback.sameContactScale",1.15f):MasterConstants.GetFloat("BattleFeedback.otherContactScale",.82f))*
                (levelUp?MasterConstants.GetFloat("BattleFeedback.skillLevelUpScale",1.2f):1)*tuning.contactEffectIntensity;
            b.root.anchoredPosition=b.origin;b.root.gameObject.SetActive(true);
            b.number.text=$"+{delta:N0}";b.number.color=same?new Color(1,.95f,.4f):Color.white;
            AnimateBurst(b,0);
        }
        public void LockTotal(int total)
        {
            charging=true;lastTarget=total;lockAge=0;
            number.text=total.ToString("N0");AnimateDamageCounter();
            chargeStart=shotPosition;
            celebration.text=$"{total:N0} DAMAGE";celebrateAge=0;celebration.color=gold;
        }
        public void Impact(bool isSkill,bool isEnemy,bool support)
        {
            HideFlight();
            charging=false;chargeOrb.color=Color.clear;impactAge=0;
            impactCenter=new Vector2(360,(support?!isEnemy:isEnemy)?387:882);
            impactBurst.rectTransform.anchoredPosition=impactCenter;
            impactBurst.color=support?new Color(.6f,1,.68f):Color.white;
            bool targetBottom=support?!isEnemy:isEnemy;
            impactRing.rectTransform.anchoredPosition=targetBottom?new Vector2(360,278):new Vector2(530,1115);
            impactRing.color=support?new Color(.12f,.78f,.4f):new Color(1,.42f,.03f);
            Vector2 delta=impactRing.rectTransform.anchoredPosition-bankCenter;
            beam.rectTransform.anchoredPosition=bankCenter;beam.rectTransform.sizeDelta=new Vector2(delta.magnitude,55);
            beam.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
            showBeam=false;beam.color=Color.clear;
        }
        public void HideFlight()
        {
            foreach(var t in flightTail)t.color=Color.clear;
            flightCore.color=flightGlow.color=Color.clear;flightNumber.gameObject.SetActive(false);
        }
        public void AnimateDamageFlight(float progress,bool isEnemy)
        {
            charging=false;chargeOrb.color=Color.clear;
            Vector2 destination=isEnemy?new Vector2(360,278):new Vector2(530,1115);
            float p=Mathf.SmoothStep(0,1,progress);
            Vector2 Position(float t)=>Vector2.Lerp(bankCenter,destination,t)+Vector2.down*Mathf.Sin(t*Mathf.PI)*(isEnemy?40:95);
            flightGlow.rectTransform.anchoredPosition=flightCore.rectTransform.anchoredPosition=Position(p);
            flightGlow.color=new Color(1,.68f,.04f,.9f);flightCore.color=Color.white;
            flightCore.rectTransform.localRotation=Quaternion.Euler(0,0,progress*340);
            flightNumber.gameObject.SetActive(true);flightNumber.text=lastTarget.ToString("N0");
            flightNumber.rectTransform.anchoredPosition=Position(p)+Vector2.up*52;
            flightNumber.rectTransform.localScale=Vector3.one*(1+Mathf.Sin(progress*Mathf.PI)*.15f);
            for(int i=0;i<flightTail.Length;i++)
            {
                float t=Mathf.Max(0,p-i*.024f);flightTail[i].rectTransform.anchoredPosition=Position(t);
                flightTail[i].rectTransform.sizeDelta=Vector2.one*(120-i*8);
                flightTail[i].color=new Color(1,.62f,.06f,(1-i/10f)*.7f);
            }
        }
        void Update()
        {
            if(tuning==null || paused)return;
            float dt=Mathf.Min(Time.deltaTime,.05f);damagePulseAge+=dt;impactAge+=dt;celebrateAge+=dt;lockAge+=dt;
            if(AllClearVisible)
            {
                allClearAge+=dt;
                float p=Mathf.Clamp01(allClearAge/tuning.allClearBannerSeconds);
                allClear.rectTransform.anchoredPosition=new Vector2(Mathf.Lerp(930,-210,p),100);
                if(p>=1)allClear.gameObject.SetActive(false);
            }
            AnimateDamageCounter();
            float cp=Mathf.Clamp01(celebrateAge/MasterConstants.GetFloat("BattleFeedback.celebrationSeconds",.72f));celebration.alpha=1-cp;
            celebration.rectTransform.anchoredPosition=new Vector2(360,879+cp*16);
            celebration.rectTransform.localScale=Vector3.one*(1+.35f*Mathf.Exp(-cp*7));
            celebration.rectTransform.localRotation=Quaternion.Euler(0,0,-4*(1-cp));
            if(charging)
            {
                float p=Mathf.Clamp01(lockAge/Mathf.Max(.05f,tuning.chargeSeconds));
                chargeOrb.rectTransform.anchoredPosition=Vector2.Lerp(chargeStart,bankCenter,p*p)+Vector2.left*Mathf.Sin(p*Mathf.PI)*45;
                chargeOrb.rectTransform.localRotation=Quaternion.Euler(0,0,p*240);
                chargeOrb.rectTransform.sizeDelta=Vector2.one*Mathf.Lerp(35,95,p);
                chargeOrb.color=Alpha(Color.white,Mathf.Sin(p*Mathf.PI));
            }
            foreach(var b in bursts)if(b.active)AnimateBurst(b,dt);
            AnimateImpact();
        }
        void AnimateBurst(Burst b,float dt)
        {
            b.age+=dt;float p=Mathf.Clamp01(b.age/Mathf.Max(.05f,tuning.contactEffectSeconds));
            if(p>=1){b.active=false;b.root.gameObject.SetActive(false);return;}
            float ease=1-Mathf.Pow(1-p,3),opacity=Mathf.Clamp01(tuning.contactEffectIntensity);
            b.glow.color=Alpha(b.color,(1-p)*.75f*opacity);b.glow.rectTransform.sizeDelta=Vector2.one*(100+140*ease)*b.scale;
            b.blast.color=Alpha(Color.white,Mathf.Max(0,1-p*3)*.85f*opacity);
            b.blast.rectTransform.sizeDelta=Vector2.one*Mathf.Lerp(35,135*b.scale,ease);
            b.blast.rectTransform.localRotation=Quaternion.Euler(0,0,b.rotation);
            b.ring.color=Alpha(b.color,(1-p)*opacity);b.ring.rectTransform.sizeDelta=Vector2.one*Mathf.Lerp(35,180*b.scale,ease);
            b.echo.color=Alpha(Color.white,Mathf.Max(0,1-p*1.6f)*opacity);b.echo.rectTransform.sizeDelta=Vector2.one*Mathf.Lerp(18,125*b.scale,ease);
            for(int i=0;i<b.sparks.Length;i++)
            {
                float angle=(i*30+b.rotation)*Mathf.Deg2Rad;var s=b.sparks[i];
                s.rectTransform.anchoredPosition=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Mathf.Lerp(12,(i%2==0?112:80)*b.scale,ease)+Vector2.down*p*p*28;
                s.rectTransform.sizeDelta=new Vector2((i%3==0?28:14)*(1-p)*b.scale,(i%3==0?28:36)*(1-p)*b.scale);
                s.rectTransform.localRotation=Quaternion.Euler(0,0,angle*Mathf.Rad2Deg-90+p*160);
                s.color=Alpha(i%3==0?Color.white:b.color,(1-p)*opacity);
            }
            b.number.rectTransform.anchoredPosition=new Vector2(0,30+ease*55);
            b.number.rectTransform.localScale=Vector3.one*(1+.6f*Mathf.Exp(-p*12));b.number.alpha=1-p*p;
            for(int i=0;i<b.motes.Length;i++)
            {
                float fly=Mathf.Clamp01((p-i*.045f)/.86f),eased=fly*fly*(3-2*fly);var mote=b.motes[i];
                mote.rectTransform.anchoredPosition=Vector2.Lerp(Vector2.zero,bankCenter-b.origin,eased)+new Vector2((i-1.5f)*30,60+i*10)*Mathf.Sin(fly*Mathf.PI);
                mote.rectTransform.sizeDelta=Vector2.one*Mathf.Lerp(26,9,fly);mote.rectTransform.localRotation=Quaternion.Euler(0,0,fly*250);
                mote.color=Alpha(i%2==0?Color.white:b.color,Mathf.Sin(fly*Mathf.PI)*opacity);
            }
        }
        void AnimateImpact()
        {
            float p=Mathf.Clamp01(impactAge/MasterConstants.GetFloat("BattleFeedback.impactVisualSeconds",.55f)),fade=1-p,ease=1-Mathf.Pow(1-p,3);
            impactRing.rectTransform.sizeDelta=Vector2.one*Mathf.Lerp(45,285,ease);impactRing.color=Alpha(impactRing.color,fade*.85f*tuning.contactEffectIntensity);
            impactBurst.rectTransform.sizeDelta=new Vector2(490,205)*(1+.2f*ease);
            impactBurst.color=Alpha(impactBurst.color,fade*.6f);
            beam.color=Alpha(gold,showBeam?Mathf.Max(0,1-impactAge/.24f):0);
            for(int i=0;i<impactRays.Length;i++)
            {
                float angle=i*Mathf.PI*2/impactRays.Length;var ray=impactRays[i];
                ray.rectTransform.anchoredPosition=impactCenter+new Vector2(Mathf.Cos(angle)*250,Mathf.Sin(angle)*110)*ease;
                ray.rectTransform.localRotation=Quaternion.Euler(0,0,angle*Mathf.Rad2Deg-90);
                ray.rectTransform.sizeDelta=new Vector2(7,80*fade);ray.color=Alpha(i%2==0?Color.white:gold,fade*.85f);
            }
        }
    }
}
