using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;

namespace Ohajiki.Engine;

[AttributeUsage(AttributeTargets.Field)]
public sealed class ExposeAttribute(string label, string description = "") : Attribute
{
    public string Label { get; } = label;
    public string Description { get; } = description;
}

public sealed class InputState
{
    public Vector2 Pointer { get; internal set; }
    public int Wheel {get;internal set;}
    public Vector2 PressPosition { get; internal set; }
    public bool Pressed { get; internal set; }
    public bool Held { get; internal set; }
    public bool Released { get; internal set; }
    readonly HashSet<Keys> keys = new();
    public bool Key(Keys key) => keys.Contains(key);
    internal void Press(Vector2 position) { Pointer = PressPosition = position; Pressed = Held = true; }
    internal void SetKey(Keys key, bool down) { if (down) keys.Add(key); else keys.Remove(key); }
    internal void EndFrame() { Pressed = Released = false; Wheel=0; }
    internal void Clear() { keys.Clear(); Pressed = Held = Released = false; Wheel=0; }
}

public abstract class OhajikiBehaviour
{
    public GameObject GameObject { get; internal set; } = null!;
    public Transform2D Transform => GameObject.Transform;
    public SceneData Scene { get; internal set; } = null!;
    public InputState Input { get; internal set; } = null!;
    public float DeltaTime { get; internal set; }
    internal Action<string> Logger { get; set; } = _ => { };
    internal Action<string> EventSender { get; set; } = _ => { };
    public void Log(string message) => Logger(message);
    public void SendEvent(string name) => EventSender(name);
    public virtual void OnStart() { }
    public virtual void OnUpdate() { }
    public virtual void OnCollision(GameObject other) { }
    public virtual void OnStop() { }
}

sealed class ScriptLoadContext() : AssemblyLoadContext(isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName) => assemblyName.Name == typeof(OhajikiBehaviour).Assembly.GetName().Name ? typeof(OhajikiBehaviour).Assembly : null;
}

public sealed class ScriptCompiler : IDisposable
{
    ScriptLoadContext? context;
    public Dictionary<string, Type> Types { get; private set; } = new();
    public string Diagnostics { get; private set; } = "";
    public bool Compile(EngineProject project)
    {
        var files = Directory.GetFiles(project.Assets, "*.cs", SearchOption.AllDirectories).OrderBy(f => f).ToArray();
        if (files.Length == 0) { Dispose(); Diagnostics = "C#スクリプトはありません。"; return true; }
        var syntax = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), new CSharpParseOptions(LanguageVersion.Latest), f)).ToArray();
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator).Append(typeof(OhajikiBehaviour).Assembly.Location).Distinct();
        var refs = paths.Where(File.Exists).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("OhajikiScripts_" + Guid.NewGuid().ToString("N"), syntax, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        using var stream = new MemoryStream(); var emit = compilation.Emit(stream);
        Diagnostics = string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString()));
        if (!emit.Success) return false;
        var next = new ScriptLoadContext();
        try
        {
            stream.Position = 0; var assembly = next.LoadFromStream(stream);
            var types = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(OhajikiBehaviour).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null).ToDictionary(t => t.FullName!);
            var previous = context; Types = types; context = next; previous?.Unload();
            Diagnostics = $"C#コンパイル成功：{files.Length}ファイル / {Types.Count}コンポーネント" + (Diagnostics.Length == 0 ? "" : "\n" + Diagnostics); return true;
        }
        catch (Exception ex) { next.Unload(); Diagnostics = ex.ToString(); return false; }
    }
    public static IEnumerable<FieldInfo> Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => !f.IsInitOnly && Supported(f.FieldType));
    public static bool Supported(Type type) => type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(double) || type.IsEnum;
    public static object? Parse(string value, Type type) => type.IsEnum ? Enum.Parse(type, value) : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    public OhajikiBehaviour Create(ComponentData data, GameObject obj, SceneData scene, InputState input, Action<string> log, Action<string> send)
    {
        var name = data.Get("Class"); if (!Types.TryGetValue(name, out var type)) throw new InvalidDataException($"スクリプトクラスが見つかりません：{name}");
        var behaviour = (OhajikiBehaviour)Activator.CreateInstance(type)!;
        behaviour.GameObject = obj; behaviour.Scene = scene; behaviour.Input = input; behaviour.Logger = log; behaviour.EventSender = send;
        foreach (var field in Fields(type)) if (data.Values.TryGetValue("field:" + field.Name, out var value)) field.SetValue(behaviour, Parse(value, field.FieldType));
        return behaviour;
    }
    public void Dispose() { Types.Clear(); context?.Unload(); context = null; }
    public static string Template(string className) => $$"""
        using Ohajiki.Engine;

        // 公開フィールドはインスペクターから調整・保存できます。
        public sealed class {{className}} : OhajikiBehaviour
        {
            [Expose("回転速度", "1秒あたりの回転角度")]
            public float Speed = 30f;

            public override void OnStart()
            {
                Log(GameObject.Name + " を開始しました");
            }

            public override void OnUpdate()
            {
                Transform.Rotation += Speed * DeltaTime;
            }

            public override void OnCollision(GameObject other) { }
            public override void OnStop() { }
        }
        """;
}
