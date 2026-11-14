using System.Text.Json;

namespace Ohajiki;

// Only durable progress is stored. Screen state, drafts, quotes and unfinished
// battles are deliberately excluded; their already committed costs remain saved.
public sealed record SpiritSave(string Id, int Level, int Copies, long Experience);
public sealed record ProgressSave(int Rank, long TotalExperience, long RankExperience, long Coins);
public sealed record ShopSave(Dictionary<string, int> Purchases, DateTime PremiumMonth, DateTime LastClaim);
public sealed class PlayerSave
{
    public required SpiritSave[] Spirits { get; init; }
    public required string[][] Decks { get; init; }
    public required int ActiveDeck { get; init; }
    public required int Energy { get; init; }
    public required int Gems { get; init; }
    public required int ShiningStones { get; init; }
    public required int EventPoints { get; init; }
    public required int[] Materials { get; init; }
    public required ProgressSave Progress { get; init; }
    public required string[] ClearedQuests { get; init; }
    public required string[] SingleDiscounts { get; init; }
    public required Dictionary<string, int> GachaUses { get; init; }
    public required ShopSave Shop { get; init; }
    public required ulong RandomState { get; init; }
    internal static readonly JsonSerializerOptions Json = new() { RespectRequiredConstructorParameters = true };
    internal string Encode() => JsonSerializer.Serialize(this, Json);
    internal static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException("セーブデータを復元できません：" + message); }
    internal static void Counts(Dictionary<string, int>? values)
    { Require(values != null && values.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value >= 0), "利用回数が不正です。"); }
}

public sealed partial class PlayerSession
{
    string SpiritId(int index) => Config.catalog[index].masterId;
    string BannerId(int index) => Config.banners[index].masterId;
    public PlayerSave CaptureSave() => new()
    {
        Spirits = Enumerable.Range(0, levels.Length).Select(i => new SpiritSave(SpiritId(i), levels[i], copies[i], spiritExperience[i])).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray(),
        Decks = decks.Select(deck => deck.Select(SpiritId).ToArray()).ToArray(), ActiveDeck = ActiveDeck,
        Energy = Energy, Gems = Gems, ShiningStones = ShiningStones, EventPoints = EventPoints,
        Materials = (int[])materials.Clone(), Progress = Progress.CaptureSave(),
        ClearedQuests = clearedQuests.Order(StringComparer.Ordinal).ToArray(),
        SingleDiscounts = usedSingleDiscounts.Select(BannerId).Concat(retiredDiscounts).Distinct().Order(StringComparer.Ordinal).ToArray(),
        GachaUses = gachaPlayUses.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(),
        Shop = Shop.CaptureSave(), RandomState = ((SessionRandom)random).State
    };
    readonly HashSet<string> retiredDiscounts = new(StringComparer.Ordinal);

    public void RestoreSave(PlayerSave save, BattleTuning tuning)
    {
        // Validate the whole document before changing the live session.
        PlayerSave.Require(save.Spirits != null && save.Spirits.All(s => s != null && !string.IsNullOrWhiteSpace(s.Id)) && save.Spirits.Select(s => s.Id).Distinct().Count() == save.Spirits.Length, "スピリターIDが不正です。");
        var byId = Config.catalog.Select((s, i) => (s.masterId, i)).ToDictionary(s => s.masterId, s => s.i, StringComparer.Ordinal);
        var restored = save.Spirits!.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var s in save.Spirits!)
        {
            PlayerSave.Require(byId.TryGetValue(s.Id, out var i), $"MasterDataにスピリター {s.Id} がありません。元のMasterDataを戻してください。");
            PlayerSave.Require(s.Level >= 1 && s.Level <= MaximumSpiritLevel(i) && s.Copies >= 0 && s.Experience >= 0, "育成・所持数が不正、またはMasterDataの上限と一致しません。");
            var definition = Config.catalog[i]; bool specific = definition.experienceBase > 0;
            long next = (long)(specific ? definition.experienceBase : Config.spiritExperienceBase) + (long)(specific ? definition.experiencePerLevel : Config.spiritExperiencePerLevel) * (s.Level - 1);
            PlayerSave.Require(s.Level == MaximumSpiritLevel(i) ? s.Experience == 0 : s.Experience < next, "スピリター経験値が上限を超えています。");
        }
        PlayerSave.Require(save.Decks != null && save.Decks.Length == decks.Length && save.ActiveDeck >= 0 && save.ActiveDeck < decks.Length, "デッキ枠が一致しません。");
        foreach (var deck in save.Decks!)
            PlayerSave.Require(deck != null && deck.Length == SpiritDeck.Size && deck.Distinct().Count() == SpiritDeck.Size && deck.All(id => id != null && restored.TryGetValue(id, out var s) && s.Copies > 0), "デッキに未所持または重複したスピリターがあります。");
        PlayerSave.Require(save.Energy >= 0 && save.Gems >= 0 && save.ShiningStones >= 0 && save.EventPoints >= 0 && save.Materials != null && save.Materials.Length == materials.Length && save.Materials.All(n => n >= 0), "通貨・素材・エネルギーが不正です。");
        PlayerProgress.ValidateSave(save.Progress, tuning);
        PlayerSave.Require(save.ClearedQuests != null && save.ClearedQuests.All(id => !string.IsNullOrWhiteSpace(id)) && save.SingleDiscounts != null && save.SingleDiscounts.All(id => !string.IsNullOrWhiteSpace(id)), "クエスト・ガチャIDが不正です。");
        PlayerSave.Counts(save.GachaUses); ShopState.ValidateSave(save.Shop);

        // New master entries are unowned; initial gifts are never granted again.
        Array.Clear(copies); Array.Clear(spiritExperience);
        foreach (var s in save.Spirits) { int i = byId[s.Id]; levels[i] = s.Level; copies[i] = s.Copies; spiritExperience[i] = s.Experience; }
        for (int i = 0; i < decks.Length; i++) decks[i] = save.Decks[i].Select(id => byId[id]).ToArray();
        ActiveDeck = save.ActiveDeck; Energy = save.Energy; Gems = save.Gems; ShiningStones = save.ShiningStones; EventPoints = save.EventPoints;
        save.Materials!.CopyTo(materials, 0); Progress.RestoreSave(save.Progress);
        clearedQuests.Clear(); clearedQuests.UnionWith(save.ClearedQuests!); ActiveQuestRun = null;
        usedSingleDiscounts.Clear(); retiredDiscounts.Clear();
        foreach (string id in save.SingleDiscounts!) { int index = Array.FindIndex(Config.banners, b => b.masterId == id); if (index >= 0) usedSingleDiscounts.Add(index); else retiredDiscounts.Add(id); }
        gachaPlayUses.Clear(); foreach (var pair in save.GachaUses) gachaPlayUses.Add(pair.Key, pair.Value);
        Shop.RestoreSave(save.Shop); ((SessionRandom)random).State = save.RandomState;
    }
}

public sealed partial class PlayerProgress
{
    internal ProgressSave CaptureSave() => new(Rank, TotalExperience, RankExperience, Coins);
    internal static void ValidateSave(ProgressSave? save, BattleTuning tuning)
    {
        PlayerSave.Require(save != null && save.Rank >= 1 && save.Rank <= tuning.maxRank && save.TotalExperience >= 0 && save.RankExperience >= 0 && save.RankExperience <= save.TotalExperience && save.Coins >= 0, "プレイヤーランク・経験値・メダルが不正です。");
        var candidate = new PlayerProgress(); candidate.RestoreSave(save!);
        PlayerSave.Require(candidate.Rank == tuning.maxRank || candidate.RankExperience < candidate.Needed(tuning), "ランク経験値が上限を超えています。");
    }
    internal void RestoreSave(ProgressSave save)
    { Rank = save.Rank; TotalExperience = save.TotalExperience; RankExperience = save.RankExperience; Coins = save.Coins; }
}

public sealed partial class ShopState
{
    internal ShopSave CaptureSave() => new(purchases.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(), premiumMonth, lastClaim);
    internal static void ValidateSave(ShopSave? save)
    {
        PlayerSave.Require(save != null, "ショップ履歴がありません。"); PlayerSave.Counts(save!.Purchases);
        PlayerSave.Require(save.PremiumMonth.TimeOfDay == TimeSpan.Zero && save.PremiumMonth.Day == 1 && save.LastClaim.TimeOfDay == TimeSpan.Zero, "プレミアムの受取日が不正です。");
    }
    internal void RestoreSave(ShopSave save)
    {
        purchases.Clear(); foreach (var pair in save.Purchases) purchases.Add(pair.Key, pair.Value);
        premiumMonth = save.PremiumMonth; lastClaim = save.LastClaim; observedDay = Today; pending = null;
    }
}

// PCG-XSH-RR: fixed algorithm and serializable state independent of .NET's Random.
// This continues gacha/drop rolls after restart, rather than reseeding each launch.
internal sealed class SessionRandom : Random
{
    internal ulong State { get; set; }
    internal SessionRandom(int seed) { State = unchecked((ulong)(long)seed); Step(); }
    uint Step()
    {
        ulong previous = State; State = unchecked(previous * 6364136223846793005UL + 1442695040888963407UL);
        uint bits = (uint)(((previous >> 18) ^ previous) >> 27); int shift = (int)(previous >> 59);
        return (bits >> shift) | (bits << ((-shift) & 31));
    }
    protected override double Sample() => NextDouble();
    public override double NextDouble() => ((Step() >> 5) * 67108864.0 + (Step() >> 6)) / 9007199254740992.0;
    public override int Next() => Next(int.MaxValue);
    public override int Next(int maxValue) => Next(0, maxValue);
    public override int Next(int minValue, int maxValue)
    {
        if (maxValue < minValue) throw new ArgumentOutOfRangeException(nameof(maxValue));
        uint bound = (uint)((long)maxValue - minValue); if (bound == 0) return minValue;
        uint threshold = unchecked(0u - bound) % bound, value; do { value = Step(); } while (value < threshold);
        return (int)(minValue + (long)(value % bound));
    }
}
