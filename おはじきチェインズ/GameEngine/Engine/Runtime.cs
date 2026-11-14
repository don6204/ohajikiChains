using System.Numerics;
using System.Media;

namespace Ohajiki.Engine;

public sealed class Particle
{
    public Vector2 Position, Velocity;
    public float Age, Lifetime, Size, EndSize, Gravity;
    public Color Color;
    public string Texture = "";
}

public sealed class RuntimeWorld : IDisposable
{
    public SceneData Scene { get; private set; }
    public GameApplication? Game { get; }
    public InputState Input { get; } = new();
    public List<Particle> Particles { get; } = new();
    public int Hits { get; private set; }
    public int SameElementHits { get; private set; }
    public float WeightedContacts { get; private set; }
    public float AttackValue { get; private set; }
    public float Fade { get; private set; }
    public (Vector2 Start, Vector2 End)? Aim { get; private set; }
    public event Action? SceneChanged;
    public event Action<GameObject, GameObject>? Collision;
    readonly EngineProject project;
    readonly ScriptCompiler scripts;
    readonly Action<string> log;
    readonly Dictionary<string, Vector2> velocities = new();
    readonly Dictionary<string, float> multipliers = new();
    readonly Dictionary<string, float> emitterTime = new();
    readonly List<(OhajikiBehaviour Behaviour, ComponentData Component)> behaviours = new();
    readonly List<SoundPlayer> sounds = new();
    readonly HashSet<string> failed = new();
    readonly List<string> eventQueue = new();
    readonly Dictionary<string, bool> originalChargeStates = new();
    readonly HashSet<string> originalIds = new();
    readonly Random random = new(1401);
    float accumulator;
    bool shotInProgress;
    GameObject? aimed;
    Vector2 pressPoint;
    SceneData? transitionScene;
    float transitionTime, transitionDuration;
    bool transitionSwitched;
    public RuntimeWorld(EngineProject project, SceneData scene, ScriptCompiler scripts, Action<string> logger)
    {
        this.project = project; Scene = JsonIO.Clone(scene); this.scripts = scripts; log = logger;
        if(Scene.Objects.Any(o=>o.GetComponent("OhajikiGame")!=null))Game=new(project,Scene,Input,log,saveDirectory:project.Resolve("SaveData"));else Initialize();
    }
    void Initialize()
    {
        velocities.Clear(); multipliers.Clear(); emitterTime.Clear(); originalChargeStates.Clear(); originalIds.Clear(); Particles.Clear(); Input.Clear(); aimed = null; Aim = null;
        Hits = SameElementHits = 0; WeightedContacts = AttackValue = 0; accumulator = 0; shotInProgress = false;
        foreach (var obj in Scene.Objects.ToArray())
        {
            originalIds.Add(obj.Id);
            if (obj.GetComponent("Charge") != null) originalChargeStates[obj.Id] = obj.Active;
            if (obj.GetComponent("RigidBody") is { } body) velocities[obj.Id] = new(body.Number("VelocityX"), body.Number("VelocityY"));
            if (!Scene.IsActive(obj)) continue;
            foreach (var component in obj.Components.Where(c => c.Enabled))
            {
                if (component.Type == "Script")
                {
                    var behaviour = scripts.Create(component, obj, Scene, Input, log, SendEvent); behaviours.Add((behaviour, component)); Invoke(behaviour, component.Id, b => b.OnStart());
                }
                if (component.Type == "Audio" && component.Get("File").Length > 0)
                {
                    try { var sound = new SoundPlayer(project.Resolve(component.Get("File"))); sound.Load(); if (component.Flag("Loop")) sound.PlayLooping(); else sound.Play(); sounds.Add(sound); }
                    catch (Exception ex) { log("効果音：" + ex.Message); }
                }
            }
        }
    }
    void Invoke(OhajikiBehaviour behaviour, string key, Action<OhajikiBehaviour> action)
    {
        if (failed.Contains(key)) return;
        try { action(behaviour); } catch (Exception ex) { failed.Add(key); log($"スクリプト停止 [{behaviour.GameObject.Name}]：{ex}"); }
    }
    public void SendEvent(string eventName) => eventQueue.Add(eventName);
    public void SetVelocity(GameObject obj, Vector2 value) => velocities[obj.Id] = value;
    public Vector2 GetVelocity(GameObject obj) => velocities.GetValueOrDefault(obj.Id);
    public bool Launch(GameObject obj, Vector2 direction)
    {
        if (shotInProgress || direction.LengthSquared() < .001f || obj.GetComponent("Shot") is not { } shot) return false;
        Hits = SameElementHits = 0; WeightedContacts = AttackValue = 0; multipliers.Clear();
        velocities[obj.Id] = Vector2.Normalize(direction) * Math.Clamp(shot.Number("Speed", 760), 1, 12000); multipliers[obj.Id] = 1; shotInProgress = true; log("試射開始：" + obj.Name); return true;
    }
    public void Update(float elapsed)
    {
        if(Game!=null){Game.Update(elapsed);Input.EndFrame();return;}
        elapsed = Math.Clamp(elapsed, 0, .1f);
        if (transitionScene != null)
        {
            transitionTime += elapsed;
            if (!transitionSwitched && transitionTime >= transitionDuration / 2)
            {
                StopBehaviours(); Scene = transitionScene; transitionSwitched = true; Initialize(); SceneChanged?.Invoke();
            }
            Fade = Math.Clamp(transitionTime < transitionDuration / 2 ? transitionTime / (transitionDuration / 2) : 2 - transitionTime / (transitionDuration / 2), 0, 1);
            if (transitionTime >= transitionDuration) { transitionScene = null; Fade = 0; }
            Input.EndFrame(); return;
        }
        HandleInput();
        foreach (var (b, component) in behaviours.ToArray()) if (component.Enabled && Scene.Objects.Contains(b.GameObject) && Scene.IsActive(b.GameObject)) { b.DeltaTime = elapsed; Invoke(b, component.Id, x => x.OnUpdate()); }
        accumulator += elapsed;
        const float fixedStep = 1f / 120;
        while (accumulator >= fixedStep) { Physics(fixedStep); accumulator -= fixedStep; }
        UpdateParticles(elapsed);
        foreach (var obj in Scene.Objects)
        {
            if (obj.GetComponent("BattleReadout") is not { } hud || obj.GetComponent("Text") is not { } text) continue;
            int level = 1; for (int i = 2; i <= 5; i++) if (SameElementHits >= hud.Integer("Level" + i, new[] { 0, 0, 1, 3, 5, 10 }[i])) level = i;
            text.Set("Text", hud.Get("Value") switch { "効果度" => "効果度  Lv." + level, "攻撃値" => "攻撃値  " + AttackValue.ToString("0"), _ => Hits + " HIT" });
        }
        Input.EndFrame();
        if (eventQueue.Count > 0) { var name = eventQueue[0]; eventQueue.Clear(); BeginTransition(name); }
    }
    void HandleInput()
    {
        if (Input.Pressed)
        {
            var button = Scene.Objects.OrderBy(o => o.Order).LastOrDefault(o => o.GetComponent("Button") != null && SceneRenderer.Hit(Scene, o, Input.PressPosition));
            if (button != null) { SendEvent(button.GetComponent("Button")!.Get("Event")); return; }
            if (!shotInProgress)
            {
                aimed = Scene.Objects.LastOrDefault(o => o.GetComponent("Shot") != null && Scene.IsActive(o));
                if (aimed != null)
                {
                    var shot = aimed.GetComponent("Shot")!; var bounds = Bounds();
                    var pos = new Vector2(Math.Clamp(Input.PressPosition.X, bounds.Left + 32, bounds.Right - 32), shot.Number("LaunchY", Scene.Position(aimed).Y));
                    if (Math.Abs(Input.PressPosition.Y - pos.Y) > 100) { aimed = null; return; }
                    Scene.SetWorldPosition(aimed, pos); pressPoint = Input.PressPosition;
                }
            }
        }
        if (aimed != null && Input.Held) { var pos = Scene.Position(aimed); Aim = (pos, pos + Input.Pointer - pressPoint); }
        if (aimed != null && Input.Released)
        {
            var delta = Input.Pointer - pressPoint;
            if (delta.Length() >= aimed.GetComponent("Shot")!.Number("MinDrag", 20)) Launch(aimed, delta);
            aimed = null; Aim = null;
        }
    }
    RectangleF Bounds()
    {
        var board = Scene.Objects.FirstOrDefault(o => Scene.IsActive(o) && o.GetComponent("Board") != null);
        if (board == null) return new(0, 0, Scene.Width, Scene.Height);
        var pos = Scene.Position(board); var m = Scene.Matrix(board); var w = Math.Abs(board.Transform.Width * m.M11); var h = Math.Abs(board.Transform.Height * m.M22);
        return new(pos.X - w / 2, pos.Y - h / 2, w, h);
    }
    Vector2 HalfSize(GameObject obj)
    {
        var c = obj.GetComponent("BoxCollider"); var m = Scene.Matrix(obj);
        return new(Math.Abs((c?.Number("Width", obj.Transform.Width) ?? obj.Transform.Width) * new Vector2(m.M11, m.M12).Length()) / 2,
            Math.Abs((c?.Number("Height", obj.Transform.Height) ?? obj.Transform.Height) * new Vector2(m.M21, m.M22).Length()) / 2);
    }
    void Physics(float dt)
    {
        var bounds = Bounds(); bool moving = false;
        foreach (var obj in Scene.Objects.ToArray())
        {
            if (!Scene.IsActive(obj) || obj.GetComponent("RigidBody") is not { } body) continue;
            var velocity = velocities.GetValueOrDefault(obj.Id, new(body.Number("VelocityX"), body.Number("VelocityY")));
            if (velocity.LengthSquared() < .01f) continue;
            moving = true; var half = HalfSize(obj); var speed = velocity.Length(); if (speed > 12000) velocity *= 12000 / speed;
            var steps = Math.Clamp((int)Math.Ceiling(velocity.Length() * dt / Math.Max(1, Math.Min(half.X, half.Y))), 1, 128);
            for (int step = 0; step < steps; step++)
            {
                var pos = Scene.Position(obj) + velocity * (dt / steps); var bounce = Math.Clamp(body.Number("Bounce", 1), 0, 2);
                var minX = bounds.Left + half.X; var maxX = Math.Max(minX, bounds.Right - half.X); var minY = bounds.Top + half.Y; var maxY = Math.Max(minY, bounds.Bottom - half.Y);
                if (pos.X < minX || pos.X > maxX) { pos.X = Math.Clamp(pos.X, minX, maxX); velocity.X = (pos.X <= minX ? Math.Abs(velocity.X) : -Math.Abs(velocity.X)) * bounce; }
                if (pos.Y < minY || pos.Y > maxY) { pos.Y = Math.Clamp(pos.Y, minY, maxY); velocity.Y = (pos.Y <= minY ? Math.Abs(velocity.Y) : -Math.Abs(velocity.Y)) * bounce; }
                Scene.SetWorldPosition(obj, pos);
                foreach (var other in Scene.Objects.ToArray())
                {
                    if (other == obj || !Scene.IsActive(other) || other.GetComponent("BoxCollider") is not { } collider || other.GetComponent("RigidBody") != null) continue;
                    var diff = pos - Scene.Position(other); var sum = half + HalfSize(other); if (Math.Abs(diff.X) >= sum.X || Math.Abs(diff.Y) >= sum.Y) continue;
                    Collision?.Invoke(obj, other);
                    foreach (var (b, c) in behaviours.ToArray()) if (b.GameObject == obj || b.GameObject == other) Invoke(b, c.Id, x => x.OnCollision(b.GameObject == obj ? other : obj));
                    if (!collider.Flag("Trigger"))
                    {
                        var penetration = sum - Vector2.Abs(diff);
                        if (penetration.X < penetration.Y) { velocity.X = (diff.X < 0 ? -Math.Abs(velocity.X) : Math.Abs(velocity.X)) * bounce; pos.X += (diff.X < 0 ? -1 : 1) * (penetration.X + .01f); }
                        else { velocity.Y = (diff.Y < 0 ? -Math.Abs(velocity.Y) : Math.Abs(velocity.Y)) * bounce; pos.Y += (diff.Y < 0 ? -1 : 1) * (penetration.Y + .01f); }
                        Scene.SetWorldPosition(obj, pos);
                    }
                    if (other.GetComponent("Charge") != null) Collect(obj, other, ref velocity);
                }
            }
            velocity *= MathF.Exp(-Math.Max(0, body.Number("Drag", .45f)) * dt);
            if (velocity.Length() < Math.Max(0, body.Number("StopSpeed", 20))) velocity = Vector2.Zero;
            velocities[obj.Id] = velocity;
        }
        if (shotInProgress && !moving)
        {
            shotInProgress = false; log($"試射完了：{Hits}ヒット / 同属性{SameElementHits} / 接触攻撃値 {AttackValue:0.##}");
            var board = Scene.Objects.FirstOrDefault(o => o.GetComponent("Board") != null)?.GetComponent("Board");
            if (board?.Flag("Refill", true) == true) foreach (var obj in Scene.Objects) if (originalChargeStates.TryGetValue(obj.Id, out var active)) obj.Active = active;
            Scene.Objects.RemoveAll(o => !originalIds.Contains(o.Id) && o.GetComponent("Shot") == null && o.GetComponent("RigidBody") != null);
            foreach (var obj in Scene.Objects) if (obj.GetComponent("Shot") is { } shot) { var pos = Scene.Position(obj); Scene.SetWorldPosition(obj, new(pos.X, shot.Number("LaunchY", pos.Y))); }
        }
    }
    void Collect(GameObject spiritObj, GameObject first, ref Vector2 velocity)
    {
        var queue = new Queue<GameObject>(); queue.Enqueue(first);
        while (queue.Count > 0)
        {
            var obj = queue.Dequeue(); if (!Scene.IsActive(obj) || obj.GetComponent("Charge") is not { } charge) continue;
            obj.Active = false; Hits++; var spirit = spiritObj.GetComponent("Spirit"); if (spirit != null && charge.Get("Element") == spirit.Get("Element")) SameElementHits++;
            var multiplier = multipliers.GetValueOrDefault(spiritObj.Id, 1); WeightedContacts += multiplier; AttackValue = WeightedContacts * (spirit?.Number("Attack", 100) ?? 100) * (spirit?.Number("ContactRate", .15f) ?? .15f);
            Burst(Scene.Position(obj), SceneRenderer.ParseColor(obj.GetComponent("Sprite")?.Get("Color", "#FFCF68") ?? "#FFCF68"), 12);
            switch (charge.Get("Effect"))
            {
                case "攻撃力アップ": multipliers[spiritObj.Id] = Math.Min(100, multiplier * Math.Max(0, charge.Number("AttackBoost", 1.5f))); break;
                case "速度アップ": velocity *= Math.Clamp(charge.Number("SpeedBoost", 2.5f), 0, 10); break;
                case "ミサイル縦": case "ミサイル横":
                    var key = charge.Get("Effect") == "ミサイル縦" ? "Column" : "Row";
                    foreach (var other in Scene.Objects) if (other.Active && other.GetComponent("Charge") is { } c && c.Integer(key) == charge.Integer(key)) queue.Enqueue(other);
                    break;
                case "ワープ": var bounds = Bounds(); Scene.SetWorldPosition(spiritObj, new(bounds.Left + bounds.Width / 2, bounds.Top + 36)); break;
                case "分裂":
                    var count = Math.Clamp(charge.Integer("SplitCount", 3) - 1, 0, 15);
                    for (int i = 0; i < count && Scene.Objects.Count(o => o.GetComponent("RigidBody") != null) < 64; i++)
                    {
                        var clone = JsonIO.Clone(spiritObj); clone.Id = Guid.NewGuid().ToString("N"); clone.ParentId = null; clone.Name = "分裂体";
                        foreach (var c in clone.Components) c.Id = Guid.NewGuid().ToString("N"); clone.Components.RemoveAll(c => c.Type is "Shot" or "Script");
                        var p = Scene.Position(spiritObj); clone.Transform.X = p.X; clone.Transform.Y = p.Y; clone.GetComponent("Sprite")?.Set("Opacity", .5f); Scene.Objects.Add(clone);
                        velocities[clone.Id] = Vector2.Transform(velocity, Matrix3x2.CreateRotation((i + 1) * MathF.Tau / (count + 1))); multipliers[clone.Id] = multiplier;
                    }
                    break;
            }
        }
    }
    void UpdateParticles(float dt)
    {
        foreach (var obj in Scene.Objects)
        {
            if (!Scene.IsActive(obj) || obj.GetComponent("ParticleEmitter") is not { } e) continue;
            var accumulated = emitterTime.GetValueOrDefault(e.Id) + dt * Math.Clamp(e.Number("Rate", 20), 0, 5000); int count = (int)accumulated; emitterTime[e.Id] = accumulated - count;
            for (int i = 0; i < count && Particles.Count < 10000; i++)
            {
                float angle = (e.Number("Direction", -90) + ((float)random.NextDouble() - .5f) * e.Number("Spread", 360)) * MathF.PI / 180;
                Particles.Add(new() { Position = Scene.Position(obj), Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * e.Number("Speed", 90), Lifetime = Math.Clamp(e.Number("Lifetime", .8f), .02f, 60), Size = e.Number("Size", 12), EndSize = e.Number("EndSize", 2), Gravity = e.Number("Gravity", 40), Color = SceneRenderer.ParseColor(e.Get("Color", "#FFCF68")), Texture = e.Get("Texture") });
            }
        }
        foreach (var p in Particles) { p.Age += dt; p.Velocity.Y += p.Gravity * dt; p.Position += p.Velocity * dt; } Particles.RemoveAll(p => p.Age >= p.Lifetime);
    }
    public void Burst(Vector2 at, Color color, int count)
    {
        for (int i = 0; i < count && Particles.Count < 10000; i++) { float angle = (float)random.NextDouble() * MathF.Tau; Particles.Add(new() { Position = at, Velocity = new(MathF.Cos(angle) * 100, MathF.Sin(angle) * 100), Lifetime = .45f, Size = 10, EndSize = 1, Color = color }); }
    }
    void BeginTransition(string name)
    {
        var transition = Scene.Transitions.FirstOrDefault(t => t.Event == name);
        if (transition == null) { log("シーン遷移が未登録です：" + name); return; }
        try { transitionScene = project.LoadScene(transition.Target); transitionDuration = Math.Max(.02f, transition.FadeSeconds); transitionTime = 0; transitionSwitched = false; log("シーン遷移：" + transitionScene.Name); }
        catch (Exception ex) { transitionScene = null; log("シーン遷移失敗：" + ex.Message); }
    }
    void StopBehaviours()
    {
        foreach (var (b, _) in behaviours) { try { b.OnStop(); } catch (Exception ex) { log(ex.Message); } } behaviours.Clear(); failed.Clear();
        foreach (var sound in sounds) { sound.Stop(); sound.Dispose(); } sounds.Clear();
    }
    public void Dispose() { Game?.Dispose(); StopBehaviours(); Particles.Clear(); Input.Clear(); }
}

public static class BoardTools
{
    public static void Generate(SceneData scene, GameObject board)
    {
        var config = board.GetComponent("Board") ?? throw new InvalidOperationException("盤面コンポーネントが必要です。");
        int rows = Math.Clamp(config.Integer("Rows", 8), 1, 64), columns = Math.Clamp(config.Integer("Columns", 8), 1, 64);
        float size = Math.Max(2, config.Number("ChargeSize", 44)), height = board.Transform.Height * Math.Clamp(config.Number("FillHeight", .68f), .05f, 1);
        float spacingX = board.Transform.Width / columns, spacingY = height / rows;
        if (size > spacingX || size > spacingY) throw new InvalidOperationException("ボールが重なります。列数・行数・ボールサイズを調整してください。");
        var owned = scene.Objects.Where(o => o.ParentId == board.Id && o.GetComponent("Charge") != null).ToArray(); foreach (var o in owned) scene.Remove(o);
        var random = new Random(config.Integer("Seed", 12345)); string[] elements = ["火", "水", "木", "無"]; string[] colors = ["#EF7866", "#65BAE3", "#A7CC6B", "#C7C4B8"];
        for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
        {
            var obj = scene.CreateObject($"チャージ {row + 1:D2}-{col + 1:D2}"); obj.ParentId = board.Id; obj.Order = 10;
            obj.Transform = new() { X = -board.Transform.Width / 2 + spacingX * (col + .5f), Y = -height / 2 + spacingY * (row + .5f), Width = size, Height = size };
            int element = random.Next(4); var sprite = obj.AddComponent("Sprite"); sprite.Set("Shape", "円"); sprite.Set("Color", colors[element]); var collider = obj.AddComponent("BoxCollider"); collider.Set("Width", size); collider.Set("Height", size);
            var charge = obj.AddComponent("Charge"); charge.Set("Element", elements[element]); charge.Set("Row", row); charge.Set("Column", col);
        }
    }
}
