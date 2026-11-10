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
        sealed class Trail {public UnityEngine.UI.Image image;public float age;public bool active;}
        HubWidgets ui;
        SpiritUiSkin skin;
        BattleTuning tuning;
        RectTransform board,bank;
        UnityEngine.UI.Image bankBurst,bankShadow,bankGlow,impactRing,impactBurst,beam,chargeOrb;
        UnityEngine.UI.Image[] bankStars,impactRays;
        UnityEngine.UI.Image flightCore,flightGlow;
        UnityEngine.UI.Image[] flightTail;
        TMP_Text flightNumber;
        TMP_Text title,number,increment,hits,levelLabel,celebration;
        Burst[] bursts;
        readonly Trail[] trail=new Trail[24];
        readonly Vector2 bankCenter=new Vector2(530,1080);
        readonly Color gold=new Color(1,.82f,.12f);
        readonly Color[] colors={new Color(1,.27f,.055f),new Color(.08f,.68f,1),new Color(.28f,.90f,.17f),new Color(.70f,.3f,1)};
        Vector2 shotPosition,chargeStart,impactCenter;
        Color shotColor;
        int burstCursor,trailCursor,lastTarget,lastContacts,lastLevel=1,lastTier,lastDisplayed=-1;
        float displayed,punch,hitAge=10,impactAge=10,trailTimer,clock,celebrateAge=10,lockAge=10;
        bool enemy,paused,locked,moving,showBeam,charging,lockedPop;
        public int DamageTarget=>lastTarget;
        public int ContactCount=>lastContacts;
        public float DisplayedDamage=>displayed;
        public Vector3 BankScale=>bank.localScale;
        public bool DamageBankVisible=>bank.gameObject.activeInHierarchy;
        public Color DamageBankColor=>bankBurst.color;
        public void SetDamageBankVisible(bool visible)=>bank.gameObject.SetActive(visible);
        public int ActiveBursts {get {int n=0;foreach(var b in bursts)if(b.active)n++;return n;}}
        public int PoolCapacity=>bursts.Length;
        public void Initialize(RectTransform board,TMP_FontAsset font,SpiritUiSkin skin,BattleTuning config)
        {
            this.board=board;this.skin=skin;tuning=config;ui=new HubWidgets(font);
            for(int i=0;i<trail.Length;i++)trail[i]=new Trail{image=Graphic(transform,"Spirit afterglow",skin.Glow,Vector2.zero,50)};
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
            bank=ui.Rect(transform,"Damage bank",bankCenter.x,bankCenter.y,298,160);bank.pivot=new Vector2(.5f,.5f);
            bankGlow=Graphic(bank,"Energy halo",skin.Glow,new Vector2(149,80),280);
            bankStars=new UnityEngine.UI.Image[10];
            for(int i=0;i<bankStars.Length;i++)bankStars[i]=Graphic(bank,"Counter spark",skin.Star,Vector2.zero,22);
            bankShadow=Graphic(bank,"Damage burst outline",skin.Burst,new Vector2(149,77),1);bankShadow.rectTransform.sizeDelta=new Vector2(318,167);
            var rim=Graphic(bank,"Golden damage rim",skin.Burst,new Vector2(149,82),1);
            rim.rectTransform.sizeDelta=new Vector2(312,163);rim.color=Color.white;
            bankBurst=Graphic(bank,"Damage burst",skin.DamageBurst,new Vector2(149,82),1);bankBurst.rectTransform.sizeDelta=new Vector2(304,158);
            title=ui.Label(bank,"通常 / 防御前",30,128,145,27,12,SpiritUiSkin.Ink);
            number=Label(bank,"0",new Vector2(149,83),new Vector2(244,96),83,Color.white);
            number.enableAutoSizing=true;number.fontSizeMin=34;number.fontSizeMax=83;
            number.enableVertexGradient=true;number.colorGradient=new VertexGradient(Color.white,Color.white,new Color(1,.76f,.12f),new Color(1,.76f,.12f));
            increment=Label(bank,"",new Vector2(220,139),new Vector2(104,48),25,Color.white);
            hits=ui.Label(bank,"0 HIT",32,0,117,46,25,SpiritUiSkin.Ink);
            levelLabel=ui.Label(bank,"SKILL Lv1",153,9,130,29,18,SpiritUiSkin.Ink,true);
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
            foreach(var t in trail){t.active=false;t.image.color=Color.clear;}
            foreach(var ray in impactRays)ray.color=Color.clear;
            foreach(var star in bankStars)star.color=Color.clear;
            moving=locked=paused=enemy=showBeam=charging=lockedPop=false;
            lastTarget=lastContacts=lastTier=0;lastLevel=1;lastDisplayed=-1;displayed=punch=clock=trailTimer=0;
            hitAge=impactAge=celebrateAge=lockAge=10;
            bank.anchoredPosition=bankCenter;bank.localScale=Vector3.one;bank.localRotation=Quaternion.identity;
            number.text="0";number.rectTransform.localScale=Vector3.one;
            title.text="通常 / 防御前";hits.text="0 HIT";levelLabel.text="SKILL Lv1";increment.text=celebration.text="";
            bankGlow.color=impactRing.color=impactBurst.color=beam.color=chargeOrb.color=Color.clear;
            bankShadow.color=new Color(.33f,.16f,.04f);UpdateBankColor();
            HideFlight();
            SetDamageBankVisible(false);
        }
        public void BeginShot(bool isEnemy,Element element)
        {ResetFeedback();enemy=isEnemy;shotColor=colors[(int)element];moving=true;title.text=enemy?"敵 通常 / 防御前":"通常 / 防御前";}
        public void MoveShot(Vector2 position)=>shotPosition=board.anchoredPosition+position;
        public void SetPaused(bool value)=>paused=value;
        void UpdateBankColor()
        {
            // Follow the rolling number, with a warm coral cap rather than saturated red.
            float heat=Mathf.Clamp01(displayed/Mathf.Max(1,tuning.damageColorFullAt));
            Color yellow=new Color(1,.90f,.35f),orange=new Color(1,.65f,.24f),coral=new Color(1,.43f,.34f);
            bankBurst.color=heat<.5f?Color.Lerp(yellow,orange,heat*2):Color.Lerp(orange,coral,(heat-.5f)*2);
            Color cream=Color.Lerp(new Color(1,.82f,.28f),new Color(1,.94f,.76f),heat);
            number.colorGradient=new VertexGradient(Color.white,Color.white,cream,cream);
        }
        public void Contact(Vector2 position,Element element,bool same,int contacts,int matched,int total)
        {
            int level=BattleRules.EffectLevel(matched,tuning),delta=total-lastTarget,tier=0;
            if(tuning.contactCelebrationThresholds!=null)foreach(int threshold in tuning.contactCelebrationThresholds)if(contacts>=threshold)tier++;
            bool levelUp=level>lastLevel;
            if(tier>lastTier || levelUp)
            {
                celebration.text=tier>lastTier?$"{contacts} CHAIN!":$"SKILL Lv{level} UP!";
                celebrateAge=0;celebration.color=tier>lastTier?gold:new Color(.55f,1,.64f);
            }
            lastTarget=total;lastContacts=contacts;lastLevel=level;lastTier=tier;
            hits.text=$"{contacts} HIT";levelLabel.text=$"SKILL Lv{level}";increment.text=$"+{delta:N0}";
            punch=levelUp?1.25f:1;hitAge=0;
            var b=bursts[burstCursor++%bursts.Length];b.age=0;b.rotation=contacts*29;b.active=true;
            b.origin=board.anchoredPosition+position;b.color=colors[(int)element];
            b.scale=(same?1.15f:.82f)*(levelUp?1.2f:1)*tuning.contactEffectIntensity;
            b.root.anchoredPosition=b.origin;b.root.gameObject.SetActive(true);
            b.number.text=$"+{delta:N0}";b.number.color=same?new Color(1,.95f,.4f):Color.white;
            AnimateBurst(b,0);
        }
        public void LockTotal(int total)
        {
            moving=false;locked=charging=true;lastTarget=total;lockAge=0;lockedPop=false;punch=1.4f;hitAge=0;
            chargeStart=shotPosition;title.text="TOTAL DAMAGE";increment.text="確定";
            celebration.text=$"{lastContacts} HIT  TOTAL!";celebrateAge=0;celebration.color=gold;
        }
        public void Impact(bool isSkill,bool isEnemy,bool support)
        {
            HideFlight();
            charging=moving=false;chargeOrb.color=Color.clear;displayed=lastTarget;impactAge=0;
            impactCenter=new Vector2(360,(support?!isEnemy:isEnemy)?387:882);
            impactBurst.rectTransform.anchoredPosition=impactCenter;
            impactBurst.color=support?new Color(.6f,1,.68f):Color.white;
            impactRing.rectTransform.anchoredPosition=new Vector2(support?(isEnemy?190:360):(isEnemy?360:190),support?(isEnemy?1068:274):(isEnemy?274:1068));
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
            charging=false;chargeOrb.color=Color.clear;displayed=lastTarget;
            Vector2 destination=isEnemy?new Vector2(360,278):new Vector2(190,1070);
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
            float dt=Mathf.Min(Time.deltaTime,.05f);clock+=dt;hitAge+=dt;impactAge+=dt;celebrateAge+=dt;lockAge+=dt;
            punch=Mathf.MoveTowards(punch,0,dt*2.5f);
            displayed=Mathf.MoveTowards(displayed,lastTarget,Mathf.Max(1,lastTarget-displayed)*tuning.damageCounterSpeed*dt);
            if(Mathf.Abs(displayed-lastTarget)<1)displayed=lastTarget;
            int value=Mathf.RoundToInt(displayed);if(lastDisplayed!=value){number.text=value.ToString("N0");lastDisplayed=value;}
            float kick=punch*Mathf.Exp(-hitAge*1.5f),pulse=kick*Mathf.Cos(hitAge*34);
            Vector2 shake=new Vector2(Mathf.Sin(hitAge*83),Mathf.Cos(hitAge*65)*.7f)*kick*tuning.damageBankShake;
            bank.anchoredPosition=new Vector2(Mathf.Clamp(bankCenter.x+shake.x,510,540),Mathf.Clamp(bankCenter.y+shake.y,1055,1082));
            bank.localRotation=Quaternion.Euler(0,0,Mathf.Sin(hitAge*53)*kick*tuning.damageBankTilt);
            bank.localScale=new Vector3(1+pulse*.055f,1+pulse*.15f,1);
            number.rectTransform.localScale=new Vector3(1+kick*tuning.damageNumberPunch*(.65f+.35f*Mathf.Cos(hitAge*38)),1+kick*tuning.damageNumberPunch*(.7f+.3f*Mathf.Sin(hitAge*38)),1);
            increment.rectTransform.anchoredPosition=new Vector2(220,139+Mathf.Sin(Mathf.Clamp01(hitAge/.4f)*Mathf.PI)*7);
            increment.rectTransform.localScale=Vector3.one*(1+kick*.15f);
            if(hitAge>.65f&&!locked)increment.text="";
            UpdateBankColor();
            bankBurst.rectTransform.localScale=Vector3.one*(1+kick*.045f);
            bankGlow.color=Alpha(bankBurst.color,kick*.65f);
            for(int i=0;i<bankStars.Length;i++)
            {
                float angle=i*Mathf.PI*2/bankStars.Length+clock*.7f;
                bankStars[i].rectTransform.anchoredPosition=new Vector2(149+Mathf.Cos(angle)*(128+hitAge*60),80+Mathf.Sin(angle)*(62+hitAge*30));
                bankStars[i].rectTransform.localRotation=Quaternion.Euler(0,0,clock*110+i*30);
                bankStars[i].color=Alpha(i%2==0?Color.white:gold,Mathf.Max(0,1-hitAge/.48f)*tuning.contactEffectIntensity);
            }
            float cp=Mathf.Clamp01(celebrateAge/.72f);celebration.alpha=1-cp;
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
                if(p>.8f&&!lockedPop){lockedPop=true;punch=1.45f;hitAge=0;}
            }
            foreach(var b in bursts)if(b.active)AnimateBurst(b,dt);
            foreach(var t in trail)if(t.active)
            {
                t.age+=dt;float alpha=1-t.age/.3f;t.active=alpha>0;
                t.image.color=Alpha(shotColor,alpha*tuning.trailOpacity);
                t.image.rectTransform.localScale=Vector3.one*Mathf.Max(.1f,alpha);
            }
            trailTimer-=dt;if(moving&&trailTimer<=0)
            {trailTimer=.012f;var t=trail[trailCursor++%trail.Length];t.age=0;t.active=true;t.image.rectTransform.anchoredPosition=shotPosition;t.image.rectTransform.sizeDelta=Vector2.one*tuning.shotRadius*3.1f;}
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
            float p=Mathf.Clamp01(impactAge/.55f),fade=1-p,ease=1-Mathf.Pow(1-p,3);
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
