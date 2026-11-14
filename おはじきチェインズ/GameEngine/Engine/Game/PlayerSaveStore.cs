using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ohajiki.Engine;

// One writer per profile. The lock survives neither process exit nor a crash.
public sealed class PlayerSaveStore : IDisposable
{
    const int Version = 1;
    readonly FileStream profileLock;
    readonly string path, backup;
    string? lastPayload;
    public bool RecoveredBackup { get; private set; }
    public string FilePath => path;
    public PlayerSaveStore(string directory)
    {
        Directory.CreateDirectory(directory); path = Path.Combine(directory, "player.json"); backup = path + ".bak";
        try { profileLock = new(Path.Combine(directory, "player.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("セーブデータを使用できません。同じゲームのプレイヤー／エディターが起動中なら終了してください。\n" + directory, ex); }
    }
    static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    static (PlayerSave Save, string Payload) Decode(string file)
    {
        if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException("セーブデータが大きすぎます。");
        using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8)); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number)) throw new InvalidDataException("セーブ形式が不正です。");
        // A newer writer is not corruption. Never silently replace its data with an old backup.
        if (number != Version) throw new NotSupportedException($"セーブ形式 {number} はこのアプリでは読み込めません。対応するバージョンで起動してください。");
        if (!root.TryGetProperty("Data", out var data) || !root.TryGetProperty("Sha256", out var checksum) || checksum.ValueKind != JsonValueKind.String) throw new InvalidDataException("セーブデータが欠けています。");
        string payload = data.GetRawText();
        if (!string.Equals(checksum.GetString(), Hash(payload), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("セーブデータのチェックサムが一致しません。");
        return (JsonSerializer.Deserialize<PlayerSave>(payload, PlayerSave.Json) ?? throw new InvalidDataException("進行データがありません。"), payload);
    }
    public PlayerSave? Load()
    {
        if (File.Exists(path))
        {
            try { var loaded = Decode(path); lastPayload = loaded.Payload; return loaded.Save; }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { /* try the last complete save */ }
        }
        else if (!File.Exists(backup)) return null;
        if (!File.Exists(backup)) throw new InvalidDataException("セーブデータが破損しており、バックアップがありません。SaveDataを保管して確認してください。");
        (PlayerSave Save, string Payload) recovered;
        try { recovered = Decode(backup); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { throw new InvalidDataException("セーブとバックアップを読み込めません。ファイルを上書きせず起動を中止しました。", ex); }
        // Restoration is only written after the game validates IDs and progression.
        RecoveredBackup = true; lastPayload = null; return recovered.Save;
    }
    public bool Save(PlayerSave save)
    {
        string payload = save.Encode(); if (payload == lastPayload) return false;
        string envelope = "{\"Version\":" + Version + ",\"SavedUtc\":" + JsonSerializer.Serialize(DateTimeOffset.UtcNow) + ",\"Sha256\":\"" + Hash(payload) + "\",\"Data\":" + payload + "}";
        string temp = path + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { byte[] bytes = Encoding.UTF8.GetBytes(envelope); output.Write(bytes); output.Flush(true); }
            if (File.Exists(path))
            {
                if (RecoveredBackup) File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
                File.Replace(temp, path, RecoveredBackup ? null : backup, true);
            }
            else File.Move(temp, path);
            // Also protect the very first save, before any gameplay mutation.
            if (!File.Exists(backup)) File.Copy(path, backup, false);
            lastPayload = payload; RecoveredBackup = false; return true;
        }
        finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
    }
    public void Dispose() => profileLock.Dispose();
}
