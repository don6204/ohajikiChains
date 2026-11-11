using System;
using System.Collections.Generic;
using System.Linq;

namespace Ohajiki
{
    public sealed class QuestRun
    {
        public QuestDefinition Quest { get; }
        public IReadOnlyList<int> Participants { get; }
        internal QuestRun(QuestDefinition quest,int[] participants)
        { Quest=UnityEngine.JsonUtility.FromJson<QuestDefinition>(UnityEngine.JsonUtility.ToJson(quest));Participants=Array.AsReadOnly(participants); }
    }
    public sealed class QuestReceipt
    {
        public bool Won { get; internal set; }
        public bool FirstClear { get; internal set; }
        public int SpiritIndex { get; internal set; } = -1;
        public string Summary { get; internal set; }
    }
    public sealed partial class PlayerSession
    {
        readonly HashSet<string> clearedQuests=new HashSet<string>(StringComparer.Ordinal);
        public QuestRun ActiveQuestRun { get; private set; }
        public int MaximumEnergy => Config.maximumEnergy+(Progress.Rank-1)*Config.energyPerRank;
        public int FriendCapacity => Config.baseFriendCapacity+(Progress.Rank-1)*Config.friendsPerRank;
        public bool IsCleared(string id)=>clearedQuests.Contains(id);
        public bool QuestAvailable(string id,out string reason)
        {
            var quest=Config.quests?.Find(id);reason=null;
            if(quest==null){reason="クエストが見つかりません";return false;}
            if(!quest.CalendarOpen(ShopCalendar.Today(clock())))
            {reason="開催期間外";return false;}
            if(!string.IsNullOrEmpty(quest.prerequisiteId) && !IsCleared(quest.prerequisiteId))
            {reason=(Config.quests.Find(quest.prerequisiteId)?.DisplayTitle??quest.prerequisiteId)+"をクリアすると解放";return false;}
            return true;
        }
        public bool TryBeginQuest(string id,out QuestRun run,out string reason)
        {
            run=null;
            if(ActiveQuestRun!=null){reason="すでに出撃中です";return false;}
            if(!QuestAvailable(id,out reason))return false;
            var quest=Config.quests.Find(id);
            if(Energy<quest.energyCost){reason="エネルギーが不足しています";return false;}
            Energy-=quest.energyCost;
            run=ActiveQuestRun=new QuestRun(quest,DeckCopy());return true;
        }
        public void AbandonQuest(QuestRun run)
        {if(ReferenceEquals(run,ActiveQuestRun))ActiveQuestRun=null;}
        public bool TryRefillEnergy(out string reason)
        {
            reason=null;
            if(Energy>=MaximumEnergy){reason="エネルギーは満タンです";return false;}
            if(Gems<Config.energyRefillGems){reason="宝石が不足しています";return false;}
            Gems-=Config.energyRefillGems;Energy=MaximumEnergy;return true;
        }
        public QuestReceipt CompleteQuest(QuestRun run,bool won,BattleTuning tuning)
        {
            if(run==null || !ReferenceEquals(run,ActiveQuestRun))return null;
            var q=run.Quest;
            bool first=won && !IsCleared(q.id);
            if(!won){ActiveQuestRun=null;return new QuestReceipt{Summary="クリア報酬はありません。\n編成や属性を変えて再挑戦！"};}
            var rewards=q.Rewards(first);
            if(!CanApplyShop(ShopCurrency.Medals,0,rewards,out string error))throw new InvalidOperationException(error);
            // Validate before the single commit. No UI action can grant the run again.
            if(Progress.Coins>long.MaxValue-q.medals)throw new InvalidOperationException("メダル上限です");
            int spirit=-1;
            if(q.rewardSpiritIndex>=0 && ((first && q.firstClearSpirit) || random.NextDouble()<q.spiritDropChance))
            {spirit=q.rewardSpiritIndex;if(copies[spirit]==int.MaxValue)throw new InvalidOperationException("スピリター所持上限です");}
            ApplyShop(ShopCurrency.Medals,0,rewards,out _);
            Progress.Award(q.experience,q.medals,tuning);
            AwardBattleExperience(run.Participants,q.experience);
            if(spirit>=0)copies[spirit]++;
            clearedQuests.Add(q.id);ActiveQuestRun=null;
            string summary=$"{q.DisplayTitle}\nEXP +{q.experience} / メダル +{q.medals:N0}";
            int gems=q.gems+(first?q.firstClearGems:0);
            if(gems>0)summary+=$"\n宝石 +{gems}"+(first&&q.firstClearGems>0?"（初回報酬を含む）":"");
            if(q.materialCount>0)summary+=$"\n{new[]{"火","水","木","全"}[q.materialType]}パワー +{q.materialCount}";
            if(q.eventPoints>0)summary+=$"\nイベントポイント +{q.eventPoints}";
            if(q.shiningStones>0)summary+=$"\n輝く石 +{q.shiningStones}";
            if(spirit>=0)summary+="\nスピリター獲得："+Config.catalog[spirit].displayName;
            if(first)summary+="\n初回クリア！";
            return new QuestReceipt{Won=true,FirstClear=first,SpiritIndex=spirit,Summary=summary};
        }
    }
}
