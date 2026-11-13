using System;
using UnityEngine;

namespace Ohajiki
{
    // A generated, self-contained snapshot. The source workbook and prototype assets remain untouched.
    public sealed class MasterGameData : ScriptableObject
    {
        public HubConfig home;
        public BattleTuning battle;
        public MasterConstants constants;
        public string sourceFile, sourceHash, importedUtc;
        public MasterItem[] items;
        public MasterNpc[] npcs;
        public int sourceRows;
        static MasterGameData runtimeData;
        static bool runtimeLoaded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeLookup(){runtimeData=null;runtimeLoaded=false;}
        public static string ItemName(ShopRewardKind kind,string fallback)
        {
            // The current wallet combines free and paid gems; its label must not imply separate balances.
            if(!Application.isPlaying || kind==ShopRewardKind.Gems)return fallback;
            if(!runtimeLoaded){runtimeData=Resources.Load<MasterGameData>("OhajikiMasterData");runtimeLoaded=true;}
            string id;
            switch(kind)
            {
                case ShopRewardKind.Medals:id="ITEM0000_0000";break;
                case ShopRewardKind.FirePower:id="ITEM0000_0003";break;
                case ShopRewardKind.WaterPower:id="ITEM0000_0004";break;
                case ShopRewardKind.WoodPower:id="ITEM0000_0005";break;
                case ShopRewardKind.AllPower:id="ITEM0000_0006";break;
                case ShopRewardKind.ShiningStones:id="ITEM0000_0007";break;
                case ShopRewardKind.EventPoints:id="ITEM0000_0008";break;
                default:return fallback;
            }
            if(runtimeData?.items!=null)
                foreach(var item in runtimeData.items)
                    if(item!=null && item.id==id && !string.IsNullOrWhiteSpace(item.name))return item.name;
            return fallback;
        }
    }
    [Serializable] public sealed class MasterItem
    {
        public string id, name, description, useDescription;
        public Sprite texture;
        public bool hasMinimum, hasMaximum;
        public long minimum, maximum;
    }
    [Serializable] public sealed class MasterNpc
    {
        public string id, name, description, mainSpiritId;
        public bool hasType;
        public int type;
        public SpiritDefinition[] deck;
    }
}
