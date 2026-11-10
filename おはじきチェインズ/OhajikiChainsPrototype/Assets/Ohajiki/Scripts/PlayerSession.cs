using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ohajiki
{
    // One authoritative session. UI drafts and battle copies never mutate source assets.
    public sealed partial class PlayerSession
    {
        public HubConfig Config { get; }
        public PlayerProgress Progress { get; } = new PlayerProgress();
        public int ActiveDeck { get; private set; }
        public int Energy { get; private set; }
        public int Gems { get; private set; }
        public int ShiningStones { get; private set; }
        public int EventPoints { get; private set; }
        public ShopState Shop { get; }
        readonly Func<DateTimeOffset> clock;
        readonly int[] levels,copies, materials = new int[4];
        readonly System.Random random;
        readonly int[][] decks;
        public PlayerSession(HubConfig config, BattleTuning tuning,Func<DateTimeOffset> clock=null)
        {
            if (!config.Validate(out string error)) throw new ArgumentException(error);
            Config = config; Energy = config.maximumEnergy; Gems = config.startingGems;
            this.clock=clock??(()=>DateTimeOffset.UtcNow);
            levels = config.catalog.Select(s => s.level).ToArray();
            copies=Enumerable.Range(0,levels.Length).Select(i=>i<config.startingOwnedCount?1:0).ToArray();
            random=new System.Random(tuning.seed+100);
            for (int i = 0; i < materials.Length; i++) materials[i] = config.startingMaterials;
            decks = new int[config.deckSlots][];
            for (int slot = 0; slot < decks.Length; slot++)
                decks[slot] = Enumerable.Range(0, SpiritDeck.Size).Select(i => (i + slot * 3) % config.startingOwnedCount).ToArray();
            Progress.Award(0, config.startingCoins, tuning);
            if(config.shop!=null)
            {
                ShiningStones=config.shop.startingShiningStones;EventPoints=config.shop.startingEventPoints;
                Shop=new ShopState(this,config.shop,this.clock);
            }
        }
        public int Level(int index) => levels[index];
        public int Copies(int index) => copies[index];
        public int MaterialCount(int type) => materials[type];
        public int[] DeckCopy() => (int[])decks[ActiveDeck].Clone();
        public void SwitchDeck(int delta) => ActiveDeck = (ActiveDeck + delta % decks.Length + decks.Length) % decks.Length;
        public bool SaveDeck(IReadOnlyList<int> draft)
        {
            if (draft == null || draft.Count != SpiritDeck.Size || draft.Distinct().Count() != SpiritDeck.Size || draft.Any(i => i < 0 || i >= levels.Length || copies[i]==0)) return false;
            decks[ActiveDeck] = draft.ToArray(); return true;
        }
        public int Hp(int id) => Config.catalog[id].baseHp + Config.catalog[id].hpPerLevel * (levels[id] - 1);
        public int Attack(int id) => Config.catalog[id].baseAttack + Config.catalog[id].attackPerLevel * (levels[id] - 1);
        public float Speed(int id) => Config.catalog[id].baseSpeed + Config.catalog[id].speedPerLevel * (levels[id] - 1);
        public int DeckHp => decks[ActiveDeck].Sum(Hp);
        public int[] FindSpirits(int element, int rarity, int sort)
        {
            var source = Enumerable.Range(0, levels.Length).Where(i => copies[i]>0 && (element < 0 || (int)Config.catalog[i].element == element) &&
                (rarity == 0 || Config.catalog[i].rarity == rarity));
            if (sort == 1) return source.OrderBy(i => Config.catalog[i].element).ThenBy(i => Config.catalog[i].displayName).ToArray();
            if (sort == 2) return source.OrderByDescending(i => Config.catalog[i].rarity).ThenBy(i => Config.catalog[i].displayName).ToArray();
            return source.OrderBy(i => Config.catalog[i].displayName, StringComparer.Ordinal).ToArray();
        }
        public long UpgradeCost(int id, int count) => (long)Config.upgradeCoinUnit * count * (2L * levels[id] + count - 1) / 2;
        public bool CanUpgrade(int id, int material, int count)
        {
            return id >= 0 && id < levels.Length && copies[id]>0 && material >= 0 && material < materials.Length && count > 0 &&
                (material == 3 || material == (int)Config.catalog[id].element) && levels[id] + count <= Config.maxSpiritLevel &&
                materials[material] >= count && Progress.Coins >= UpgradeCost(id, count);
        }
        public bool Upgrade(int id, int material, int count)
        {
            if (!CanUpgrade(id, material, count)) return false;
            if (!Progress.TrySpendCoins(UpgradeCost(id, count))) return false;
            materials[material] -= count; levels[id] += count; return true;
        }
        public bool TryEnterBattle()
        {
            if (Energy < Config.battleEnergyCost) return false;
            Energy -= Config.battleEnergyCost; return true;
        }
        public bool BannerOpen(int index) => index>=0 && index<Config.banners.Length &&
            ShopCalendar.IsOpen(Config.banners[index].availableFrom,Config.banners[index].availableUntil,ShopCalendar.Today(clock()));
    }

    public sealed class BattleLoadout : IDisposable
    {
        public BattleTuning Tuning { get; }
        public BattleLoadout(PlayerSession session, BattleTuning template)
        {
            Tuning = UnityEngine.Object.Instantiate(template);
            Tuning.playerDeck = session.DeckCopy().Select(id => {
                var spirit = UnityEngine.Object.Instantiate(session.Config.catalog[id]);
                spirit.level = session.Level(id); return spirit;
            }).ToArray();
        }
        public void Dispose()
        {
            foreach (var spirit in Tuning.playerDeck) Release(spirit);
            Release(Tuning);
        }
        static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
