using V2=Ohajiki.Portable.Vector2;

namespace Ohajiki.Engine;

// The Unity presentation controller expressed as an ordinary managed object.
// Rules, deck drawing, collision simulation and rewards use the migrated core.
public sealed class GameBattle : IDisposable
{
    public BattleLoadout Loadout { get; }
    public BattleTuning Tuning=>Loadout.Tuning;
    public QuestRun Run { get; }
    public BattleState State { get; }
    public ShotSimulation Simulation { get; }
    public SpiritDeck Deck { get; }
    public SpiritDeck EnemyDeck { get; }
    public BattlePhase Phase { get; private set; }=BattlePhase.Ready;
    public int Selected { get; private set; }
    public bool Paused { get; set; }
    public bool EnemyAction { get; private set; }
    public SpiritDefinition Acting { get; private set; }
    public SpiritDefinition SelectedSpirit=>Tuning.playerDeck[Deck.CardAt(Selected)];
    public ActionResult LastResult { get; private set; }
    public QuestReceipt? Receipt { get; private set; }
    public float PhaseTime { get; private set; }
    public string SpecialMessage {get;private set;}="";
    public float SpecialAge {get;private set;}=10;
    public int NormalPreview=>BattleRules.ContactDamage(Acting.Attack*(EnemyAction?State.Enemy:State.Player).AttackMultiplier,Simulation.WeightedContacts,Acting.element,(EnemyAction?State.Player:State.Enemy).Element,Tuning);
    public event Action<V2,bool>? Contact;
    readonly PlayerSession session;
    readonly Random random;
    int enemySlot;
    public GameBattle(PlayerSession session, BattleTuning template, QuestRun run)
    {
        this.session=session;Run=run;Loadout=new(session,template,run.Quest);
        State=new(Tuning,run.Quest.IsPractice);Deck=new(Tuning.seed);EnemyDeck=new(Tuning.seed+1);random=new(Tuning.seed+2);
        Simulation=new(Tuning);Acting=SelectedSpirit;State.Player.Element=Acting.element;PrepareEnemy();Simulation.Prepare(false,ShotSimulation.Width/2);
        Simulation.CollectedCharge+=(i,same)=>Contact?.Invoke(Simulation.Charges[i].Position,same);
        Simulation.SpecialTriggered+=(_,special)=>{SpecialMessage=special switch{SpecialCharge.AttackBoost=>"攻撃力アップ！",SpecialCharge.SpeedBoost=>"スピードアップ！",SpecialCharge.Split=>"分裂！",SpecialCharge.VerticalMissile=>"縦ミサイル！",SpecialCharge.HorizontalMissile=>"横ミサイル！",_=>"ワープ！"};SpecialAge=0;};
        Simulation.AllCleared+=()=>{SpecialMessage="ALL CLEAR!";SpecialAge=0;};
        Simulation.TrapTriggered+=(owner,damage)=>{(owner?State.Player:State.Enemy).ReceiveTrap(damage);SpecialMessage="罠が発動！";SpecialAge=0;if(State.Finished)Simulation.CancelShot();};
    }
    void PrepareEnemy(){enemySlot=random.Next(SpiritDeck.HandSize);State.Enemy.Element=Tuning.enemyDeck[EnemyDeck.CardAt(enemySlot)].element;}
    void SetPhase(BattlePhase phase){Phase=phase;PhaseTime=0;}
    public bool Select(int slot)
    {
        if(Phase!=BattlePhase.Ready||Paused||slot<0||slot>=4)return false;
        Selected=slot;State.Player.Element=SelectedSpirit.element;Acting=SelectedSpirit;return true;
    }
    public bool Reposition(float x)
    {if(Phase!=BattlePhase.Ready||Paused)return false;Simulation.Prepare(false,x);return true;}
    public bool Shoot(V2 direction)
    {
        if(Phase!=BattlePhase.Ready||Paused||!Simulation.Launch(direction,SelectedSpirit,State.Player.SpeedMultiplier))return false;
        EnemyAction=false;Acting=SelectedSpirit;State.Player.Element=Acting.element;SetPhase(BattlePhase.PlayerShot);return true;
    }
    public void Update(float dt)
    {
        if(Paused||Phase==BattlePhase.Finished)return;
        dt=Math.Clamp(dt,0,.05f);PhaseTime+=dt;SpecialAge+=dt;
        if(Phase is BattlePhase.PlayerShot or BattlePhase.EnemyShot)
        {
            Simulation.Tick(dt);
            if(State.Finished){Finish(State.Won);return;}
            if(!Simulation.Moving)SetPhase(BattlePhase.Charging);
            return;
        }
        switch(Phase)
        {
            case BattlePhase.Charging when PhaseTime>=Tuning.chargeSeconds:SetPhase(BattlePhase.DamageFlight);break;
            case BattlePhase.DamageFlight when PhaseTime>=Tuning.damageFlightSeconds:
                LastResult=State.ResolveNormal(EnemyAction,Acting,Simulation.Collected,Simulation.Matched,Tuning,Simulation.WeightedContacts);SetPhase(BattlePhase.NormalImpact);break;
            case BattlePhase.NormalImpact when PhaseTime>=Tuning.normalImpactSeconds:SetPhase(BattlePhase.CutIn);break;
            case BattlePhase.CutIn when PhaseTime>=Tuning.cutInSeconds:LastResult=State.ResolveSkill();SetPhase(BattlePhase.Impact);break;
            case BattlePhase.Impact when PhaseTime>=Tuning.impactSeconds:CompleteAction();break;
            case BattlePhase.EnemyThinking when PhaseTime>=Tuning.enemyThinkSeconds:
                float inset=MasterConstants.GetFloat("PrototypeBattle.enemyLaunchSideInset",50);
                Simulation.Prepare(true,inset+(ShotSimulation.Width-2*inset)*(float)random.NextDouble());
                float a=(Tuning.enemyMinimumAngle+(1-2*Tuning.enemyMinimumAngle)*(float)random.NextDouble())*MathF.PI;
                EnemyAction=true;Acting=Tuning.enemyDeck[EnemyDeck.CardAt(enemySlot)];
                if(!Simulation.Launch(new V2(MathF.Cos(a),-MathF.Sin(a))*300,Acting,State.Enemy.SpeedMultiplier))throw new InvalidOperationException("敵ショットの設定が不正です");
                SetPhase(BattlePhase.EnemyShot);break;
        }
    }
    void CompleteAction()
    {
        Simulation.ResetBoard();if(EnemyAction)EnemyDeck.Replace(enemySlot);else Deck.Replace(Selected);
        if(State.Finished){Finish(State.Won);return;}
        if(State.Practice&&State.Enemy.Hp<=0){State.ContinuePractice();Simulation.Prepare(false,ShotSimulation.Width/2);Acting=SelectedSpirit;SetPhase(BattlePhase.Ready);return;}
        if(!EnemyAction){PrepareEnemy();SetPhase(BattlePhase.EnemyThinking);}
        else{Simulation.Prepare(false,ShotSimulation.Width/2);Acting=SelectedSpirit;State.Player.Element=Acting.element;SetPhase(BattlePhase.Ready);}
    }
    void Finish(bool won)
    {
        if(Phase==BattlePhase.Finished)return;
        Simulation.CancelShot();Receipt=session.CompleteQuest(Run,won,Tuning);SetPhase(BattlePhase.Finished);Paused=false;
    }
    public bool Retire()
    {
        if(State.Practice&&State.Won){Finish(true);return true;}
        session.AbandonQuest(Run);Simulation.CancelShot();return false;
    }
    public void Dispose(){session.AbandonQuest(Run);Loadout.Dispose();}
}
