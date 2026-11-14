using System.Numerics;
using PVector=Ohajiki.Portable.Vector2;

namespace Ohajiki.Engine;

public sealed partial class GameApplication
{
    static Vector2 ToBoard(PVector p)=>new(48+p.x,940-p.y);
    static PVector FromBoard(Vector2 p)=>new(p.X-48,940-p.Y);
    void HpBar(Combatant combatant,float y,string label)
    {
        Rect("HP背景",48,y,624,29,Ink);
        if(combatant.Hp>0)Rect("HP",51,y+3,618*combatant.Hp/combatant.MaxHp,23,combatant.Hp< combatant.MaxHp*.25f?Red:Teal);
        Label($"{label}  {combatant.Hp:N0} / {combatant.MaxHp:N0}",48,y-37,624,35,24,Ink,true);
    }
    void Line(Vector2 a,Vector2 b,string color,float width=4,float alpha=1)
    {
        var delta=b-a;var o=Rect("照準線",(a.X+b.X)/2-delta.Length()/2,(a.Y+b.Y)/2-width/2,delta.Length(),width,color,opacity:alpha);o.Transform.Rotation=MathF.Atan2(delta.Y,delta.X)*180/MathF.PI;
    }
    void BattleScreen()
    {
        var b=Battle!;var sim=b.Simulation;
        Rect("バトル見出し",0,0,720,96,Ink);
        Label(b.Run.Quest.DisplayTitle,14,15,545,65,29,Paper,true);
        Button("battle.pause","停止",572,20,125,57,Pause,color:Muted,font:24);
        var enemy=b.Run.Quest.enemyPortrait??b.Tuning.enemyDeck[b.EnemyDeck.CardAt(0)].portrait;
        if(enemy!=null)Image(enemy.Path,48,104,64,96);
        Button("battle.enemy","敵の詳細",130,115,260,55,()=>Information("敵のデッキ",string.Join("\n",b.Tuning.enemyDeck.Select(s=>$"{s.displayName} Lv.{s.level} {ElementNames[(int)s.element]}"))),color:Paper,font:23);
        Label($"TURN {b.State.Turn}",440,113,220,60,30,Ink,true);
        HpBar(b.State.Enemy,228,"敵");
        Image("Assets/Imported/Resources/UI/SandstoneBoard.png",42,284,636,662);
        Rect("盤面陰影",48,290,624,650,"#353D35",opacity:.48f);
        Rect("左壁",40,283,8,664,Gold);Rect("右壁",672,283,8,664,Gold);
        Rect("上壁",40,282,640,8,Gold);Rect("下壁",40,940,640,8,Gold);
        foreach(var charge in sim.Charges)
        {
            if(!charge.Active)continue;
            var p=ToBoard(charge.Position);float d=b.Tuning.chargeRadius*2;
            Rect("チャージ影",p.X-d/2+2,p.Y-d/2+3,d,d,"#172820","円",.5f);
            Rect("チャージ",p.X-d/2,p.Y-d/2,d,d,ElementColors[(int)charge.Element],"円");
            Rect("光沢",p.X-d*.25f,p.Y-d*.31f,d*.28f,d*.18f,"#FFFFFF","円",.5f);
            if(charge.Special!=SpecialCharge.None)
                Label(charge.Special switch{SpecialCharge.AttackBoost=>"攻",SpecialCharge.SpeedBoost=>"速",SpecialCharge.Split=>"分",SpecialCharge.VerticalMissile=>"↕",SpecialCharge.HorizontalMissile=>"↔",_=>"W"},p.X-d/2,p.Y-d/2,d,d,23,"#FFFFFF",true);
        }
        foreach(var trap in sim.Traps)
        {var p=ToBoard(trap.Position);Rect("罠",p.X-23,p.Y-23,46,46,ElementColors[(int)trap.TargetElement],"リング",.85f);Label("罠",p.X-25,p.Y-22,50,44,20,Paper,true);}
        foreach(var projectile in sim.Projectiles)
        {
            if(projectile.IsClone&&!projectile.Active)continue;
            var p=ToBoard(projectile.Position);float d=b.Tuning.shotRadius*2;
            Rect("スピリット球縁",p.X-d/2-3,p.Y-d/2-3,d+6,d+6,Paper,"円",projectile.IsClone?.5f:1);
            Rect("スピリット球",p.X-d/2,p.Y-d/2,d,d,ElementColors[(int)b.Acting.element],"円",projectile.IsClone?.45f:1);
            if(b.Acting.portrait!=null)Image(b.Acting.portrait.Path,p.X-d*.33f,p.Y-d/2,d*.66f,d,projectile.IsClone?.45f:.9f,"円");
        }
        foreach(var effect in contactEffects)
        {
            float t=effect.Age/Math.Max(.01f,Data.battle.contactEffectSeconds),size=(20+t*Data.battle.effectMaxDiameter)*Data.battle.contactEffectIntensity;
            Rect("接触エフェクト",effect.Point.X-size/2,effect.Point.Y-size/2,size,size,effect.Same?Gold:Paper,"リング",(1-t)*.85f);
        }
        if(aiming&&b.Phase==BattlePhase.Ready)
        {
            var origin=ToBoard(sim.Position);var delta=Logical(input.Pointer)-pressPoint;
            if(delta.Length()>1){var end=origin+Vector2.Normalize(delta)*Math.Min(180,delta.Length());Line(origin,end,Gold,6);var tangent=Vector2.Normalize(delta);var normal=new Vector2(-tangent.Y,tangent.X);Line(end,end-tangent*20+normal*12,Gold,6);Line(end,end-tangent*20-normal*12,Gold,6);}
        }
        bool showHits=b.Phase is not (BattlePhase.Ready or BattlePhase.EnemyThinking);
        if(showHits)
        {
            float hitT=Math.Clamp(hitPulseAge/.18f,0,1),pulse=1-hitT*hitT*(3-2*hitT);
            float bubbleWidth=225*(1+pulse*.10f),bubbleHeight=154*(1+pulse*.06f);
            Image("Assets/Imported/Resources/UI/HitCounterBurst.png",540-bubbleWidth/2,330-bubbleHeight/2,bubbleWidth,bubbleHeight);
            float numberScale=1+pulse*.24f;
            for(int i=0;i<5;i++)
            {float dx=i==0?-2:i==1?2:0,dy=i==2?-2:i==3?2:0;var hit=Label(sim.Collected.ToString(),448+dx,282+dy,184,96,64,i==4?"#FFD133":"#17252C",true);hit.Transform.Rotation=-17;hit.Transform.ScaleX=hit.Transform.ScaleY=numberScale;}
            float slide=Math.Min(1,b.PhaseTime*Data.battle.levelSlideSpeed/260);
            Label($"効果度 Lv.{BattleRules.EffectLevel(sim.Matched,b.Tuning)}",b.Phase is BattlePhase.PlayerShot or BattlePhase.EnemyShot?65-(1-slide)*260:65,303,300,60,30,Paper,true);
            int damage=b.NormalPreview;
            Vector2 damagePosition=new(360,864);
            if(b.Phase==BattlePhase.DamageFlight)
            {float t=Math.Clamp(b.PhaseTime/Math.Max(.01f,b.Tuning.damageFlightSeconds),0,1);damagePosition=Vector2.Lerp(damagePosition,new(360,b.EnemyAction?986:217),t*t);}
            if(b.Phase==BattlePhase.DamageFlight)
            {Rect("ダメージ枠",damagePosition.X-118,damagePosition.Y-37,236,74,Ink,opacity:.88f);var bank=Label(damage.ToString("N0"),damagePosition.X-115,damagePosition.Y-35,230,70,43,Gold,true);bank.Transform.Rotation=Data.battle.damageBankTilt;}
        }
        if(b.Phase==BattlePhase.Charging)
        {
            var p=ToBoard(sim.Position);float size=85+MathF.Sin(b.PhaseTime*9)*20;
            Rect("溜めオーラ",p.X-size/2,p.Y-size/2,size,size,Gold,"リング",.85f);
            Label("エネルギーチャージ",110,730,500,55,30,Paper,true);
        }
        if(b.Phase==BattlePhase.CutIn)
        {
            float t=Math.Clamp(b.PhaseTime/Math.Max(.01f,b.Tuning.cutInSeconds)*4,0,1),x=720*(1-t);
            Rect("スキルカットイン",x,460,720,300,Ink,opacity:.95f);Portrait(b.Acting,450+x,460,200,300);
            Label(b.Acting.displayName,x+25,485,425,75,32,Paper,true);Label(b.Acting.skillName,x+25,575,425,100,39,Gold,true);
            Label($"効果度 Lv.{BattleRules.EffectLevel(sim.Matched,b.Tuning)}",x+25,685,425,60,28,Paper);
        }
        if(b.Phase is BattlePhase.NormalImpact or BattlePhase.Impact)
        {
            int damage=b.Phase==BattlePhase.NormalImpact?b.LastResult.appliedNormalDamage:b.LastResult.appliedSkillDamage;
            Label(damage>0?$"−{damage:N0}":b.LastResult.healing>0?$"HP +{b.LastResult.healing}":b.LastResult.statusEffect??(b.LastResult.guarding?"防御を付与":""),60,b.EnemyAction?865:375,600,95,48,damage>0?Gold:"#C5EFAD",true);
        }
        if(b.SpecialAge<b.Tuning.allClearBannerSeconds)Label(b.SpecialMessage,80,650,560,90,38,Gold,true);
        HpBar(b.State.Player,1000,"あなた");
        Label(b.State.Practice&&b.State.Won?"撃破済み！「停止」→「リタイア」でクリア":b.Phase==BattlePhase.Ready?"手スピを選んで、ボールから上へスワイプ":b.EnemyAction||b.Phase==BattlePhase.EnemyThinking?"相手のターン":"あなたのターン",25,1040,670,50,23,Ink,true);
        for(int i=0;i<4;i++)
        {
            int slot=i;var spirit=b.Tuning.playerDeck[b.Deck.CardAt(slot)];float x=25+i*174;
            Rect("手スピ縁",x,1104,162,157,b.Selected==i?Gold:ElementColors[(int)spirit.element]);
            Rect("手スピ",x+4,1108,154,149,Paper);Portrait(spirit,x+49,1109,64,96);
            Label($"{ElementNames[(int)spirit.element]} Lv.{spirit.level}",x+3,1202,156,26,17,Ink,true);Label(spirit.displayName,x+3,1229,156,29,18);
            buttons.Add(new("battle.hand."+i,new(x,1104,162,157),()=>b.Select(slot),true,null));
        }
    }
    void Pause()
    {
        Confirm("一時停止","バトルを再開するには「戻る」を押してください。\n\nリタイアするとホームへ戻ります。\n\n練習クエストは、敵のHPをゼロにしてから\nリタイアするとクリアになります。",()=>Confirm("リタイア",Battle!.State.Practice&&Battle.State.Won?"練習を終えて報酬を受け取りますか？":"このバトルを終了しますか？\n消費したエネルギーは戻りません。",()=>
        {if(Battle!.Retire())Go(GameScreen.Result);else{Battle.Dispose();Battle=null;Go(GameScreen.Home);}},"終了する"),"リタイア");
    }
    void ResultScreen()
    {
        var b=Battle!;bool won=b.Receipt?.Won==true;
        Rect("リザルト地",20,100,680,1050,Paper,opacity:.96f);
        Label(won?"QUEST CLEAR!":"敗北",35,155,650,110,52,won?Teal:Red,true);
        Label(b.Run.Quest.DisplayTitle,35,290,650,80,30,Ink,true);
        Label(b.Receipt?.Summary??"バトル終了",45,400,630,410,28);
        Label($"ランク {Session.Progress.Rank}  EXP {Session.Progress.RankExperience:N0} / {Session.Progress.Needed(Data.battle):N0}\nメダル {Session.Progress.Coins:N0}  宝石 {Session.Gems}",45,840,630,100,27);
        Button("result.retry","再挑戦",50,1035,295,90,()=>BeginQuest(b.Run.Quest.id),color:Red,font:30);
        Button("result.home","ホームへ",375,1035,295,90,()=>{b.Dispose();Battle=null;Go(GameScreen.Home);},font:30);
    }
}
