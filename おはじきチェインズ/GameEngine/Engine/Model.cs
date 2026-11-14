using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ohajiki.Engine;

public sealed class Transform2D
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float Width { get; set; } = 64;
    public float Height { get; set; } = 64;
}

public sealed class ComponentData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "Sprite";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Values { get; set; } = new();
    public string Get(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
    public float Number(string key, float fallback = 0) => float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) && float.IsFinite(n) ? n : fallback;
    public int Integer(string key, int fallback = 0) => (int)Number(key, fallback);
    public bool Flag(string key, bool fallback = false) => bool.TryParse(Get(key), out bool b) ? b : fallback;
    public void Set(string key, object value) => Values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}

public sealed class GameObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "ゲームオブジェクト";
    public string? ParentId { get; set; }
    public bool Active { get; set; } = true;
    public int Order { get; set; }
    public Transform2D Transform { get; set; } = new();
    public List<ComponentData> Components { get; set; } = new();
    public ComponentData? GetComponent(string type) => Components.FirstOrDefault(c => c.Type == type && c.Enabled);
    public ComponentData AddComponent(string type)
    {
        var c = new ComponentData { Type = type };
        if (ComponentsCatalog.All.TryGetValue(type, out var definition))
            foreach (var field in definition.Fields) c.Values[field.Key] = field.Default;
        Components.Add(c); return c;
    }
}

public sealed class SceneTransition
{
    public string Event { get; set; } = "次へ";
    public string Target { get; set; } = "Assets/Scenes/Home.scene.json";
    public float FadeSeconds { get; set; } = .3f;
}

public sealed class SceneData
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "新しいシーン";
    public int Width { get; set; } = 720;
    public int Height { get; set; } = 1280;
    public string Background { get; set; } = "#10232D";
    public List<GameObject> Objects { get; set; } = new();
    public List<SceneTransition> Transitions { get; set; } = new();
    public GameObject? Find(string id) => Objects.FirstOrDefault(o => o.Id == id);
    public GameObject? FindByName(string name) => Objects.FirstOrDefault(o => o.Name == name);
    public GameObject CreateObject(string name) { var o = new GameObject { Name = name }; Objects.Add(o); return o; }
    public bool IsActive(GameObject obj) => obj.Active && (obj.ParentId == null || Find(obj.ParentId) is not { } p || IsActive(p));
    public Matrix3x2 Matrix(GameObject obj)
    {
        var t = obj.Transform;
        var local = Matrix3x2.CreateScale(t.ScaleX, t.ScaleY) * Matrix3x2.CreateRotation(t.Rotation * MathF.PI / 180) * Matrix3x2.CreateTranslation(t.X, t.Y);
        return obj.ParentId != null && Find(obj.ParentId) is { } p ? local * Matrix(p) : local;
    }
    public Vector2 Position(GameObject obj) => Vector2.Transform(Vector2.Zero, Matrix(obj));
    public void SetWorldPosition(GameObject obj, Vector2 world)
    {
        if (obj.ParentId != null && Find(obj.ParentId) is { } p && Matrix3x2.Invert(Matrix(p), out var inverse)) world = Vector2.Transform(world, inverse);
        obj.Transform.X = world.X; obj.Transform.Y = world.Y;
    }
    public HashSet<string> Descendants(string id)
    {
        var ids = new HashSet<string> { id };
        bool changed; do { changed = false; foreach (var o in Objects) if (o.ParentId != null && ids.Contains(o.ParentId)) changed |= ids.Add(o.Id); } while (changed);
        return ids;
    }
    public bool Reparent(GameObject obj, GameObject? parent)
    {
        if (parent != null && Descendants(obj.Id).Contains(parent.Id)) return false;
        var world = Matrix(obj); var local = world;
        if (parent != null) { if (!Matrix3x2.Invert(Matrix(parent), out var inverse)) return false; local = world * inverse; }
        // The editor's hierarchy supports translation, rotation and uniform parent scale. A shear is rejected.
        var x = new Vector2(local.M11, local.M12); var y = new Vector2(local.M21, local.M22);
        if (Math.Abs(Vector2.Dot(x, y)) > .001f * Math.Max(1, x.Length() * y.Length())) return false;
        obj.ParentId = parent?.Id; obj.Transform.X = local.M31; obj.Transform.Y = local.M32;
        obj.Transform.ScaleX = x.Length(); obj.Transform.ScaleY = y.Length() * Math.Sign(local.GetDeterminant());
        obj.Transform.Rotation = MathF.Atan2(local.M12, local.M11) * 180 / MathF.PI; return true;
    }
    public void Remove(GameObject obj) { var ids = Descendants(obj.Id); Objects.RemoveAll(o => ids.Contains(o.Id)); }
    public GameObject Duplicate(GameObject obj)
    {
        var ids = Descendants(obj.Id); var copies = JsonIO.Clone(Objects.Where(o => ids.Contains(o.Id)).ToList());
        var remap = copies.ToDictionary(o => o.Id, _ => Guid.NewGuid().ToString("N"));
        foreach (var copy in copies) { var oldId = copy.Id; copy.Id = remap[oldId]; if (copy.ParentId != null && remap.TryGetValue(copy.ParentId, out var parent)) copy.ParentId = parent; foreach (var c in copy.Components) c.Id = Guid.NewGuid().ToString("N"); }
        var root = copies.Single(o => o.Id == remap[obj.Id]); root.Name += " のコピー"; root.Transform.X += 24; root.Transform.Y += 24;
        Objects.AddRange(copies); return root;
    }
    public void Validate()
    {
        if (Version != 1) throw new InvalidDataException("未対応のシーン形式です。");
        if (Width < 64 || Height < 64 || Width > 8192 || Height > 8192) throw new InvalidDataException("画面サイズは64～8192の範囲にしてください。");
        if (Objects.Count > 20000) throw new InvalidDataException("シーンのオブジェクト上限は20,000です。");
        var ids = new HashSet<string>(); var componentIds = new HashSet<string>();
        foreach (var o in Objects)
        {
            if (string.IsNullOrWhiteSpace(o.Id) || !ids.Add(o.Id)) throw new InvalidDataException("オブジェクトIDが重複しています。");
            var t = o.Transform;
            if (new[] { t.X, t.Y, t.Width, t.Height, t.ScaleX, t.ScaleY, t.Rotation }.Any(n => !float.IsFinite(n))) throw new InvalidDataException("座標やサイズが数値ではありません。");
            foreach (var c in o.Components) if (!componentIds.Add(c.Id)) throw new InvalidDataException("コンポーネントIDが重複しています。");
        }
        foreach (var o in Objects)
        {
            var visited = new HashSet<string> { o.Id }; var parent = o.ParentId;
            while (parent != null) { if (!visited.Add(parent)) throw new InvalidDataException("親子関係が循環しています。"); var p = Find(parent) ?? throw new InvalidDataException("親オブジェクトがありません。"); parent = p.ParentId; }
        }
        var events = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Transitions) if (string.IsNullOrWhiteSpace(t.Event) || !events.Add(t.Event) || !float.IsFinite(t.FadeSeconds) || t.FadeSeconds < 0) throw new InvalidDataException("シーン遷移のイベント名、重複、秒数を確認してください。");
    }
}

public static class JsonIO
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNameCaseInsensitive = true };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("空のデータです。");
    public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, Serialize(value), new System.Text.UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class EditHistory
{
    readonly List<string> undo = new(); readonly List<string> redo = new();
    public void Record(SceneData scene) { var s = JsonIO.Serialize(scene); if (undo.Count == 0 || undo[^1] != s) undo.Add(s); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear(); }
    public SceneData Undo(SceneData current) { if (undo.Count == 0) return current; redo.Add(JsonIO.Serialize(current)); var result = JsonIO.Deserialize<SceneData>(undo[^1]); undo.RemoveAt(undo.Count - 1); return result; }
    public SceneData Redo(SceneData current) { if (redo.Count == 0) return current; undo.Add(JsonIO.Serialize(current)); var result = JsonIO.Deserialize<SceneData>(redo[^1]); redo.RemoveAt(redo.Count - 1); return result; }
    public void Clear() { undo.Clear(); redo.Clear(); }
}

public sealed class ProjectSettings
{
    public string Name { get; set; } = "おはじきチェインズ";
    public string StartScene { get; set; } = "Assets/Scenes/Title.scene.json";
    public string LastScene { get; set; } = "Assets/Scenes/Battle.scene.json";
}

public sealed class EngineProject
{
    public string Root { get; }
    public string Assets => Path.Combine(Root, "Assets");
    public ProjectSettings Settings { get; set; }
    public EngineProject(string root)
    {
        Root = Path.GetFullPath(root); Directory.CreateDirectory(Assets);
        var path = Resolve("Ohajiki.project.json"); Settings = File.Exists(path) ? JsonIO.Deserialize<ProjectSettings>(File.ReadAllText(path)) : new();
    }
    public string Resolve(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("プロジェクト内の相対パスを指定してください。");
        var path = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("プロジェクト外のパスは利用できません。");
        // Do not follow directory junctions out of the project during asset operations.
        for (var dir = Path.GetDirectoryName(path); dir != null && !dir.Equals(Root, StringComparison.OrdinalIgnoreCase); dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("アセット内のジャンクションは利用できません。");
        return path;
    }
    public string Relative(string path) { var relative = Path.GetRelativePath(Root, path).Replace('\\', '/'); Resolve(relative); return relative; }
    public SceneData LoadScene(string relative) { var scene = JsonIO.Deserialize<SceneData>(File.ReadAllText(Resolve(relative))); scene.Validate(); return scene; }
    public void SaveScene(string relative, SceneData scene) { scene.Validate(); JsonIO.Write(Resolve(relative), scene); }
    public void SaveSettings() => JsonIO.Write(Resolve("Ohajiki.project.json"), Settings);
    public IEnumerable<string> SceneFiles() => Directory.EnumerateFiles(Assets, "*.scene.json", SearchOption.AllDirectories).Select(Relative).OrderBy(x => x);
}

public record FieldDefinition(string Key, string Label, string Default, string Kind = "number", string Hint = "", string[]? Choices = null);
public record ComponentDefinition(string Label, string Description, FieldDefinition[] Fields);

public static class ComponentsCatalog
{
    static FieldDefinition F(string key, string label, string value, string kind = "number", string hint = "", params string[] choices) => new(key, label, value, kind, hint, choices);
    public static readonly Dictionary<string, ComponentDefinition> All = new()
    {
        ["OhajikiGame"] = new("おはじきチェインズ・ゲーム管理", "MasterDataからタイトル・編成・クエスト・ガチャ・ショップを起動します。ゲーム画面は実行時に生成します。", [F("MasterData", "MasterDataファイル", "Assets/Data/MasterData.xlsx", "asset")]),
        ["Sprite"] = new("スプライト", "画像・色・形状を表示します。PNG/JPEG/PSDの合成画像に対応。", [F("Texture", "テクスチャ", "", "texture"), F("Color", "色", "#75C7B7", "color"), F("Opacity", "不透明度", "1", hint:"0～1"), F("Shape", "形状", "矩形", "enum", "", "矩形", "円", "リング")]),
        ["Text"] = new("テキスト", "日本語を含むテキストを表示します。", [F("Text", "表示文字", "テキスト", "text"), F("FontSize", "文字サイズ", "32"), F("Color", "文字色", "#F5F0DF", "color"), F("Bold", "太字", "true", "bool")]),
        ["BoxCollider"] = new("四角形コライダー", "ゲーム仕様に合わせた軸平行の四角形判定です。回転は当たり判定に反映しません。", [F("Width", "判定の幅", "48"), F("Height", "判定の高さ", "48"), F("Trigger", "トリガー", "false", "bool")]),
        ["RigidBody"] = new("2D物理", "速度・減速・壁反射を設定します。", [F("VelocityX", "X速度", "0"), F("VelocityY", "Y速度", "0"), F("Drag", "減速率 / 秒", ".45"), F("Bounce", "反発係数", "1"), F("StopSpeed", "停止速度", "20")]),
        ["Shot"] = new("ショット操作", "ボールを左右に配置し、上方向へドラッグして発射します。", [F("Speed", "発射速度", "760"), F("MinDrag", "最小スワイプ距離", "20"), F("LaunchY", "発射Y座標", "1080")]),
        ["Spirit"] = new("スピリット", "仕様の火・水・木属性、HP、攻撃力。数値はここで調整できます。", [F("Element", "属性", "火", "enum", "", "火", "水", "木"), F("HP", "HP", "1000"), F("Attack", "攻撃力", "100"), F("ContactRate", "接触攻撃倍率", ".15"), F("Advantage", "有利属性倍率", "1.2"), F("Disadvantage", "不利属性倍率", ".8")]),
        ["Charge"] = new("チャージボール", "接触で消え、同属性接触数を加算します。特殊効果も個別に指定できます。", [F("Element", "属性", "火", "enum", "", "火", "水", "木", "無"), F("Effect", "特殊効果", "通常", "enum", "", "通常", "攻撃力アップ", "速度アップ", "分裂", "ミサイル縦", "ミサイル横", "ワープ"), F("AttackBoost", "攻撃強化倍率", "1.5"), F("SpeedBoost", "速度強化倍率", "2.5"), F("SplitCount", "分裂後の合計数", "3"), F("Row", "配置行", "0", "int"), F("Column", "配置列", "0", "int")]),
        ["Board"] = new("盤面", "物理の反射範囲と盤面生成を設定します。幅・高さはオブジェクトの値を使用。", [F("Columns", "生成列数", "8", "int"), F("Rows", "生成行数", "8", "int"), F("ChargeSize", "ボールサイズ", "44"), F("FillHeight", "生成領域の高さ割合", ".68"), F("Seed", "乱数シード", "12345", "int"), F("Refill", "停止後に空きマスを補充", "true", "bool")]),
        ["ParticleEmitter"] = new("パーティクル", "色・発生数・寿命・速度・広がりを編集して再生で確認できます。", [F("Rate", "毎秒の発生数", "20"), F("Lifetime", "寿命（秒）", ".8"), F("Speed", "速度", "90"), F("Size", "開始サイズ", "12"), F("EndSize", "終了サイズ", "2"), F("Direction", "方向（度）", "-90"), F("Spread", "広がり（度）", "360"), F("Gravity", "重力", "40"), F("Color", "色", "#FFCF68", "color"), F("Texture", "テクスチャ", "", "texture")]),
        ["Button"] = new("ボタン・シーン遷移", "再生中のクリックでイベントを送信。遷移設定のイベント名と対応させます。", [F("Event", "イベント名", "次へ", "text")]),
        ["BattleReadout"] = new("バトル数値表示", "同じオブジェクトのテキストに試射中の数値を表示します。", [F("Value", "表示対象", "ヒット数", "enum", "", "ヒット数", "効果度", "攻撃値"), F("Level2", "Lv2に必要な同属性接触数", "1", "int"), F("Level3", "Lv3に必要な同属性接触数", "3", "int"), F("Level4", "Lv4に必要な同属性接触数", "5", "int"), F("Level5", "Lv5に必要な同属性接触数", "10", "int")]),
        ["Script"] = new("C#スクリプト", "OhajikiBehaviourを継承したクラスを実行します。", [F("Class", "クラス名", "", "text")]),
        ["Audio"] = new("効果音", "開始時にWAVを再生します。", [F("File", "WAVファイル", "", "asset"), F("Loop", "ループ", "false", "bool")])
    };
}
