using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ohajiki
{
    // Imported numeric constants. The workbook remains outside the player and is never modified.
    [CreateAssetMenu(menuName="Ohajiki/Master Constants")]
    public sealed class MasterConstants : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string id;
            public double value;
        }
        public Entry[] entries=Array.Empty<Entry>();
        sealed class Rule
        {
            public double minimum,maximum;
            public bool integer;
            public Rule(double minimum,double maximum,bool integer=false)
            {this.minimum=minimum;this.maximum=maximum;this.integer=integer;}
        }
        static readonly Dictionary<string,Rule> rules=CreateRules();
        static Dictionary<string,double> cached;
        static bool loaded;
        static Dictionary<string,Rule> CreateRules()
        {
            var result=new Dictionary<string,Rule>(StringComparer.Ordinal);
            void Add(string id,double min,double max,bool integer=false)=>result.Add(id,new Rule(min,max,integer));
            Add("PlayerSession.initialOwnedCopies",1,100000,true);
            Add("PlayerSession.initialDeckOffset",0,100000,true);
            Add("PlayerSession.materialLevelGain",1,60,true);
            Add("PlayerProgress.initialRank",1,10000,true);
            // These numbers also determine UI and deck structure; reject unsupported shape changes.
            Add("SpiritDeck.Size",SpiritDeck.Size,SpiritDeck.Size,true);
            Add("SpiritDeck.HandSize",SpiritDeck.HandSize,SpiritDeck.HandSize,true);
            Add("ShotSimulation.Width",ShotSimulation.Width,ShotSimulation.Width);
            Add("ShotSimulation.Height",ShotSimulation.Height,ShotSimulation.Height);
            Add("ShopState.premiumPurchaseLimitPerMonth",1,1,true);
            Add("ShopState.premiumDailyClaimLimit",1,1,true);
            Add("BattleRules.neutralAffinity",0,100);
            Add("Combatant.minimumReductionMultiplier",.001,1);
            Add("ShotSimulation.chargeSideInset",0,ShotSimulation.Width/2-1);
            Add("ShotSimulation.launchSidePadding",0,ShotSimulation.Width/2-30);
            Add("PrototypeBattle.launchLineTouchHalfHeight",0,ShotSimulation.Height/2);
            Add("PrototypeBattle.enemyLaunchSideInset",0,ShotSimulation.Width/2);
            Add("ShopCalendar.utcOffsetHours",-14,14,true);
            Add("BattleFeedback.sameContactScale",.01,10);
            Add("BattleFeedback.otherContactScale",.01,10);
            Add("BattleFeedback.skillLevelUpScale",.01,10);
            Add("BattleFeedback.celebrationSeconds",.01,60);
            Add("BattleFeedback.impactVisualSeconds",.01,60);
            Add("BattleFeedback.damageNumberTilt",-180,180);
            Add("BattleFeedback.bankScaleThreshold1",0,int.MaxValue-1,true);
            Add("BattleFeedback.bankScaleThreshold2",1,int.MaxValue,true);
            Add("BattleFeedback.bankScale_small",.01,10);
            Add("BattleFeedback.bankScale_medium",.01,10);
            Add("BattleFeedback.bankScale_large",.01,10);
            foreach(string id in new[]{"approachSeconds","dropSeconds","releaseSeconds","revealFadeSeconds","rarityCharacterInterval","revealHaloFadeSeconds"})
                Add("GachaSequence."+id,.01,120);
            Add("GachaSequence.revealNextDelay",0,120);
            Add("GachaSequence.releaseFlashAlpha",0,1);
            Add("GachaSequence.haloMaxAlpha",0,1);
            Add("GachaSequence.revealScalePunch",0,10);
            Add("GachaSequence.revealScaleDecay",.01,100);
            Add("GachaSequence.releaseRayCount",0,256,true);
            Add("GachaStage3D.cameraFieldOfView",1,179);
            Add("GachaStage3D.releaseFieldOfView",1,179);
            Add("GachaStage3D.releaseCameraShake",0,10);
            Add("GachaStage3D.moteCount",0,256,true);
            Add("UiButtonMotion.pressedScale",.01,10);
            Add("UiButtonMotion.hoverScale",.01,10);
            Add("UiButtonMotion.scaleFollowRate",.01,1000);
            Add("TitleView.startFadeSeconds",.01,120);
            Add("TitleView.promptMinAlpha",0,1);
            Add("TitleView.promptPulseAngularRate",0,100);
            Add("TitleView.logoFloatAmplitude",0,100);
            Add("TitleView.logoFloatAngularRate",0,100);
            return result;
        }
        public static bool Supports(string id)=>id!=null&&rules.ContainsKey(id);
        public bool Validate(out string error)=>ValidateEntries(entries,out error);
        public static bool ValidateEntries(Entry[] values,out string error)
        {
            error=null;
            if(values==null){error="定数一覧がnullです";return false;}
            var seen=new Dictionary<string,double>(StringComparer.Ordinal);
            foreach(var entry in values)
            {
                if(entry==null||string.IsNullOrWhiteSpace(entry.id)||!rules.TryGetValue(entry.id,out var rule))
                {error="未対応の定数ID: "+entry?.id;return false;}
                double value=entry.value;
                if(seen.ContainsKey(entry.id)){error="定数IDが重複しています: "+entry.id;return false;}
                if(double.IsNaN(value)||double.IsInfinity(value)||value<rule.minimum||value>rule.maximum||
                    (rule.integer&&Math.Truncate(value)!=value))
                {error=entry.id+": 許容範囲 "+rule.minimum+"～"+rule.maximum+(rule.integer?"（整数）":"");return false;}
                seen.Add(entry.id,value);
            }
            double Value(string id,double fallback)=>seen.TryGetValue(id,out double value)?value:fallback;
            if(Value("BattleFeedback.bankScaleThreshold1",500)>=Value("BattleFeedback.bankScaleThreshold2",1000))
            {error="ダメージ枠のサイズ境界は第1値より第2値を大きくしてください";return false;}
            if(Value("BattleFeedback.bankScale_small",.55)>Value("BattleFeedback.bankScale_medium",.78)||
                Value("BattleFeedback.bankScale_medium",.78)>Value("BattleFeedback.bankScale_large",1))
            {error="ダメージ枠のサイズは小・中・大の順に指定してください";return false;}
            return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache(){loaded=false;cached=null;}
        static void Load()
        {
            if(loaded)return;
            var data=Resources.Load<MasterGameData>("OhajikiMasterData");
            var asset=data!=null?data.constants:null;
            if(asset!=null)
            {
                if(!asset.Validate(out string error))throw new InvalidOperationException("MasterData: "+error);
                cached=new Dictionary<string,double>(StringComparer.Ordinal);
                foreach(var entry in asset.entries)cached.Add(entry.id,entry.value);
            }
            loaded=true;
        }
        public static float GetFloat(string id,float fallback)
        {
            // EditMode unit tests use explicit model fixtures, independent of imported project data.
            if(!Application.isPlaying)return fallback;
            Load();return cached!=null&&cached.TryGetValue(id,out double value)?(float)value:fallback;
        }
        public static int GetInt(string id,int fallback)
        {
            if(!Application.isPlaying)return fallback;
            Load();return cached!=null&&cached.TryGetValue(id,out double value)?checked((int)value):fallback;
        }
    }
}
