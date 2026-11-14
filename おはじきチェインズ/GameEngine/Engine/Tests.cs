using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Ohajiki.Engine;

internal static class EngineTests
{
    public static int Run(string root)
    {
        var review = Path.Combine(root, "Review"); Directory.CreateDirectory(review); var report = Path.Combine(review, "test-results.txt");
        File.WriteAllText(report, "Ohajiki Engine validation — " + DateTimeOffset.Now.ToString("O") + "\n", Encoding.UTF8);
        int passed = 0, failed = 0;
        void Log(string text) => File.AppendAllText(report, text + "\n", Encoding.UTF8);
        void Test(string name, Action action) { try { action(); passed++; Log("PASS " + name); } catch (Exception ex) { failed++; Log("FAIL " + name + "\n" + ex); } }
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, "Expected rejection"); }
        var project = new EngineProject(Path.Combine(review, "TestProject_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"))); Templates.EnsureProject(project);
        Test("Japanese scene round-trip, component values and transitions", () =>
        {
            var scene = Templates.Battle(); scene.Transitions.Add(new() { Event = "日本語で戻る", Target = "Assets/Scenes/Home.scene.json" }); var obj = scene.FindByName("スピリットボール")!; obj.GetComponent("Spirit")!.Set("Attack", 234.5f);
            project.SaveScene("Assets/Scenes/RoundTrip.scene.json", scene); var loaded = project.LoadScene("Assets/Scenes/RoundTrip.scene.json"); Assert(JsonIO.Serialize(loaded) == JsonIO.Serialize(scene), "Serialized data mismatch");
            scene.Name = "変更後"; project.SaveScene("Assets/Scenes/RoundTrip.scene.json", scene); Assert(File.Exists(project.Resolve("Assets/Scenes/RoundTrip.scene.json.bak")), "Atomic backup missing");
        });
        Test("Hierarchy transforms, reparenting, cycle rejection", () =>
        {
            var scene = new SceneData(); var a = scene.CreateObject("親"); a.Transform.X = 100; a.Transform.Rotation = 90; var b = scene.CreateObject("子"); b.ParentId = a.Id; b.Transform.X = 10;
            Assert(Vector2.Distance(scene.Position(b), new(100, 10)) < .001f, "World transform"); Assert(!scene.Reparent(a, b), "Cycle accepted"); Assert(scene.Reparent(b, null) && Vector2.Distance(scene.Position(b), new(100, 10)) < .001f, "World position not preserved");
            a.ParentId = b.Id; b.ParentId = a.Id; Reject(scene.Validate);
        });
        Test("Deep duplication and subtree deletion preserve unrelated objects", () =>
        {
            var scene = new SceneData(); var a = scene.CreateObject("親"); var b = scene.CreateObject("子"); b.ParentId = a.Id; b.AddComponent("Sprite"); var duplicate = scene.Duplicate(a); scene.Validate(); Assert(scene.Objects.Count == 4 && scene.Descendants(duplicate.Id).Count == 2, "Deep duplicate"); scene.Remove(a); Assert(scene.Objects.Count == 2 && scene.Find(duplicate.Id) != null, "Subtree deletion");
        });
        Test("Undo / redo restore values and deletion", () =>
        {
            var scene = new SceneData(); var o = scene.CreateObject("試験"); var h = new EditHistory(); h.Record(scene); o.Transform.X = 123; h.Record(scene); scene.Remove(o); scene = h.Undo(scene); Assert(scene.Objects.Count == 1 && scene.Objects[0].Transform.X == 123, "Undo deletion"); scene = h.Undo(scene); Assert(scene.Objects[0].Transform.X == 0, "Undo move"); scene = h.Redo(scene); Assert(scene.Objects[0].Transform.X == 123, "Redo move");
        });
        Test("Path traversal and absolute asset paths rejected", () => { Reject(() => project.Resolve("../escape.cs")); Reject(() => project.Resolve("C:\\outside.png")); Assert(project.Resolve("Assets/Scripts/a.cs").StartsWith(project.Root), "Valid path failed"); });
        Test("Invalid scene versions, duplicate IDs and NaN rejected", () =>
        {
            var s = new SceneData { Version = 999 }; Reject(s.Validate); s.Version = 1; s.Width = 0; Reject(s.Validate); s.Width = 720; var o = s.CreateObject("A"); s.Objects.Add(JsonIO.Clone(o)); Reject(s.Validate); s.Objects.RemoveAt(1); o.Transform.X = float.NaN; Reject(s.Validate);
        });
        Test("Board generation is deterministic and rejects overlap atomically", () =>
        {
            var scene = Templates.Battle(); var board = scene.FindByName("盤面")!; Assert(scene.Objects.Count(o => o.GetComponent("Charge") != null) == 64, "Grid count");
            var colors = scene.Objects.Where(o => o.GetComponent("Charge") != null).Select(o => o.GetComponent("Charge")!.Get("Element")).ToArray(); BoardTools.Generate(scene, board); Assert(colors.SequenceEqual(scene.Objects.Where(o => o.GetComponent("Charge") != null).Select(o => o.GetComponent("Charge")!.Get("Element"))), "Seed changed");
            board.GetComponent("Board")!.Set("ChargeSize", 10000); int before = scene.Objects.Count; Reject(() => BoardTools.Generate(scene, board)); Assert(scene.Objects.Count == before, "Failed generation deleted charges");
        });
        Test("Rotated and scaled hit testing", () =>
        {
            var s = new SceneData(); var o = Templates.Sprite(s, "物体", 100, 100, 200, 20, "#FF0000"); o.Transform.Rotation = 90; Assert(SceneRenderer.Hit(s, o, new(100, 180)), "Rotated hit missing"); Assert(!SceneRenderer.Hit(s, o, new(180, 100)), "Rotated hit false positive"); o.Active = false; Assert(!SceneRenderer.Hit(s, o, new(100, 100)), "Inactive object picked");
        });
        Test("PNG / JPEG import and original file preservation", () =>
        {
            using var bitmap = new Bitmap(24, 16); using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.Coral);
            foreach (var ext in new[] { ".png", ".jpg" }) { var source = Path.Combine(review, "texture" + ext); bitmap.Save(source, ext == ".png" ? ImageFormat.Png : ImageFormat.Jpeg); var original = File.ReadAllBytes(source); var asset = AssetIO.Import(project, source, "Assets/Textures"); var pixels = AssetIO.ReadTexture(project.Resolve(asset)); Assert(pixels.Width == 24 && pixels.Height == 16, "Image dimensions"); Assert(original.SequenceEqual(File.ReadAllBytes(source)), "Source mutated"); Assert(AssetIO.Import(project, source, "Assets/Textures") != asset, "Duplicate import overwritten"); }
        });
        Test("PSD RGB+A raw / PackBits / ZIP / ZIP prediction", () =>
        {
            foreach (ushort compression in new ushort[] { 0, 1, 2, 3 }) { var file = PsdFixture(compression); var pixels = AssetIO.ReadPsd(file); Assert(pixels.Width == 2 && pixels.Height == 1 && pixels.Rgba.SequenceEqual(new byte[] { 255, 0, 0, 255, 0, 255, 0, 128 }), "PSD pixels / alpha mismatch " + compression); File.WriteAllBytes(project.Resolve("Assets/Textures/psd_" + compression + ".psd"), file); }
            Reject(() => AssetIO.ReadPsd(new byte[40])); var corrupt = PsdFixture(1); Reject(() => AssetIO.ReadPsd(corrupt[..^1]));
        });
        using var compiler = new ScriptCompiler();
        Test("C# compile, exposed fields, Start / Update / Collision / Stop lifecycle", () =>
        {
            File.WriteAllText(project.Resolve("Assets/Scripts/Lifecycle.cs"), "using Ohajiki.Engine; public sealed class Lifecycle : OhajikiBehaviour { [Expose(\"移動速度\")] public float Speed=2; public override void OnStart(){Transform.Y=10;} public override void OnUpdate(){Transform.X+=Speed*DeltaTime;} public override void OnCollision(GameObject other){Transform.Y=20;} public override void OnStop(){Transform.Y=30;} }");
            Assert(compiler.Compile(project), compiler.Diagnostics); var scene = new SceneData(); var o = scene.CreateObject("C#試験"); var c = o.AddComponent("Script"); c.Set("Class", "Lifecycle"); c.Set("field:Speed", 50);
            var b = compiler.Create(c, o, scene, new(), Log, _ => { }); b.OnStart(); Assert(o.Transform.Y == 10, "OnStart"); b.DeltaTime = .1f; b.OnUpdate(); Assert(Math.Abs(o.Transform.X - 5) < .001f, "Exposed field not applied"); b.OnCollision(o); Assert(o.Transform.Y == 20, "Collision"); b.OnStop(); Assert(o.Transform.Y == 30, "OnStop");
            var inspector = new InspectorObject(o, scene, compiler, () => { }, () => { }); var properties = TypeDescriptor.GetProperties(inspector); Assert(properties.Cast<PropertyDescriptor>().Any(p => p.DisplayName == "移動速度"), "Exposed field not in inspector");
        });
        Test("C# syntax diagnostics include source location; hot recompilation works", () =>
        {
            var path = project.Resolve("Assets/Scripts/Invalid.cs"); File.WriteAllText(path, "this is not C#;"); Assert(!compiler.Compile(project) && compiler.Diagnostics.Contains("Invalid.cs"), "Invalid code accepted"); File.Delete(path); Assert(compiler.Compile(project), compiler.Diagnostics);
        });
        Test("Script exceptions are logged and isolated", () =>
        {
            File.WriteAllText(project.Resolve("Assets/Scripts/Broken.cs"), "using Ohajiki.Engine; public sealed class Broken : OhajikiBehaviour {public override void OnUpdate(){throw new System.Exception(\"expected error\");}} "); Assert(compiler.Compile(project), compiler.Diagnostics);
            var scene = new SceneData(); scene.CreateObject("例外試験").AddComponent("Script").Set("Class", "Broken"); var messages = new List<string>(); using var w = new RuntimeWorld(project, scene, compiler, messages.Add); w.Update(.016f); w.Update(.016f); Assert(messages.Count(m => m.Contains("expected error")) == 1, "Exception was not isolated");
        });
        Test("Box collision collects charge once and applies contact damage", () =>
        {
            var scene = new SceneData { Width = 400, Height = 400 }; var ball = Templates.Sprite(scene, "球", 100, 200, 20, 20, "#FFFFFF"); ball.AddComponent("RigidBody").Set("Drag", 0); var bc = ball.AddComponent("BoxCollider"); bc.Set("Width", 20); bc.Set("Height", 20); ball.AddComponent("Spirit"); ball.AddComponent("Shot").Set("Speed", 500);
            var charge = Templates.Sprite(scene, "対象", 200, 200, 30, 30, "#FF0000"); var cc = charge.AddComponent("BoxCollider"); cc.Set("Width", 30); cc.Set("Height", 30); charge.AddComponent("Charge");
            using var w = new RuntimeWorld(project, scene, compiler, Log); var live = w.Scene.Find(ball.Id)!; Assert(w.Launch(live, Vector2.UnitX), "Launch rejected"); for (int i = 0; i < 35; i++) w.Update(1f / 120); Assert(w.Hits == 1 && w.SameElementHits == 1 && Math.Abs(w.AttackValue - 15) < .001f, "Contact count / damage"); Assert(w.GetVelocity(live).X < 0, "Box reflection"); Assert(scene.Find(charge.Id)!.Active, "Play mutated edit scene");
        });
        Test("All six special charge effects dispatch", () =>
        {
            foreach (var effect in new[] { "攻撃力アップ", "速度アップ", "分裂", "ミサイル縦", "ミサイル横", "ワープ" })
            {
                var s = new SceneData { Width = 600, Height = 600 }; var ball = Templates.Sprite(s, "球", 100, 250, 20, 20, "#FFFFFF"); ball.AddComponent("RigidBody").Set("Drag", 0); ball.AddComponent("BoxCollider"); ball.AddComponent("Spirit"); ball.AddComponent("Shot").Set("Speed", 500);
                foreach (var pos in new[] { new Vector2(200, 250), new Vector2(200, 400) }) { var c = Templates.Sprite(s, "対象", pos.X, pos.Y, 30, 30, "#FFFFFF"); c.AddComponent("BoxCollider"); c.AddComponent("Charge"); } s.Objects[1].GetComponent("Charge")!.Set("Effect", effect);
                using var w = new RuntimeWorld(project, s, compiler, _ => { }); var live = w.Scene.Find(ball.Id)!; w.Launch(live, Vector2.UnitX); for (int i = 0; i < 17; i++) w.Update(1f / 120);
                Assert(w.Hits >= 1, "No special collision " + effect);
                if (effect == "分裂") Assert(w.Scene.Objects.Count(o => o.GetComponent("RigidBody") != null) == 3, "Split count");
                if (effect.StartsWith("ミサイル")) Assert(w.Hits == 2, "Missile chain");
                if (effect == "速度アップ") Assert(w.GetVelocity(live).Length() > 1000, "Speed boost");
                if (effect == "ワープ") Assert(w.Scene.Position(live).Y < 100, "Warp position");
            }
        });
        Test("Short swipe retains its press position within a single frame", () =>
        {
            using var world = new RuntimeWorld(project, Templates.Battle(), compiler, Log); var ball = world.Scene.FindByName("スピリットボール")!;
            world.Input.Press(new(360, 1080)); world.Input.Pointer = new(420, 800); world.Input.Held = false; world.Input.Released = true; world.Update(1f / 60);
            Assert(world.GetVelocity(ball).Length() > 700 && world.GetVelocity(ball).Y < 0, "Short swipe was lost");
        });
        Test("Player export directory copy prevents recursive destination", () =>
        {
            Reject(() => EditorForm.CopyDirectory(project.Assets, Path.Combine(project.Assets, "nested-export")));
            var copy = Path.Combine(project.Root, "PlayerCopy"); EditorForm.CopyDirectory(project.Assets, copy); Assert(File.Exists(Path.Combine(copy, "Scenes", "Title.scene.json")), "Player scene missing"); Assert(File.Exists(Path.Combine(copy, "Scripts", "ゆっくり回転.cs")), "Player script missing");
        });
        Test("Particle spawn, aging and scene event transition", () =>
        {
            var scene = Templates.Effects(); using var w = new RuntimeWorld(project, scene, compiler, Log); for (int i = 0; i < 20; i++) w.Update(.05f); Assert(w.Particles.Count > 0 && w.Particles.Count <= 20, "Emitter lifetime / count"); w.SendEvent("戻る"); for (int i = 0; i < 20; i++) w.Update(.05f); Assert(w.Scene.Name == "ホーム" && w.Fade == 0, "Fade transition failed");
            w.SendEvent("未定義"); w.Update(.01f); Assert(w.Scene.Name == "ホーム", "Undefined transition changed scene");
        });
        Test("DirectX 12 GPU draw, texture, alpha, letterboxing, resize and readback", () =>
        {
            using var window = new Form { ClientSize = new Size(320, 240), StartPosition = FormStartPosition.Manual, Location = new Point(-12000, -12000), ShowInTaskbar = false };
            window.Show(); Application.DoEvents(); using var renderer = new D3D12Renderer(window.Handle, 320, 240, true); Log("GPU " + renderer.Adapter);
            int texture = renderer.Upload(new(2, 1, [255, 0, 0, 255, 0, 255, 0, 255])); var white = DrawCommand.Rect(160, 120, 160, 100, Color.White); white.Texture = texture;
            renderer.Render([DrawCommand.Rect(160, 120, 320, 240, Color.Blue), white], 320, 240); var capture = renderer.Capture();
            Color Pixel(TexturePixels p, int x, int y) { int i = (y * p.Width + x) * 4; return Color.FromArgb(p.Rgba[i + 3], p.Rgba[i], p.Rgba[i + 1], p.Rgba[i + 2]); }
            var left = Pixel(capture, 90, 120); var right = Pixel(capture, 230, 120); Assert(left.R > 220 && left.G < 40 && right.G > 220 && right.R < 40, "GPU texture sampling / draw not visible");
            using (var bitmap = capture.ToBitmap()) bitmap.Save(Path.Combine(review, "dx12-readback.png"));
            renderer.Render([DrawCommand.Rect(160, 120, 320, 240, Color.Blue), DrawCommand.Rect(160, 120, 100, 100, Color.Red, .5f, 1)], 320, 240); var alpha = Pixel(renderer.Capture(), 160, 120); Assert(alpha.R is > 115 and < 140 && alpha.B is > 115 and < 140, "GPU alpha blend");
            foreach (var size in new[] { new Size(256, 512), new Size(640, 200), new Size(320, 240) }) { renderer.Resize(size.Width, size.Height); renderer.Render([DrawCommand.Rect(100, 100, 200, 200, Color.Lime)], 200, 200); var p = renderer.Capture(); Assert(Pixel(p, size.Width / 2, size.Height / 2).G > 250, "Resize / center"); Assert(Pixel(p, 0, 0).G < 100, "Letterboxing"); }
            int errors = renderer.DebugErrorCount; Log("D3D12 debug errors: " + errors + (errors == -1 ? " (debug layer unavailable)" : "")); Assert(errors <= 0, "D3D12 validation errors"); window.Close();
        });
        Test("Japanese editor integration: create, inspect, undo, save, play/stop, screenshot", () =>
        {
            Log("EDITOR create form");
            using var editor = new EditorForm(project, "Assets/Scenes/Battle.scene.json") { StartPosition = FormStartPosition.Manual, Location = new Point(-12000, -12000), ShowInTaskbar = false };
            Log("EDITOR show form"); editor.Show(); Application.DoEvents(); Log("EDITOR shown"); Assert(editor.Renderer != null, "Editor renderer missing"); int count = editor.Scene.Objects.Count;
            editor.CreateObject("スプライト"); Log("EDITOR object created"); Assert(editor.Scene.Objects.Count == count + 1 && editor.Selected != null, "Create object"); editor.Selected!.Transform.X = 240; editor.Selected.Transform.Rotation = 35; editor.Selected.AddComponent("ParticleEmitter");
            var props = TypeDescriptor.GetProperties(new InspectorObject(editor.Selected, editor.Scene, editor.Compiler, () => { }, () => { })); props["Transform.ScaleX"]!.SetValue(null, 1.5f); Assert(editor.Selected.Transform.ScaleX == 1.5f, "Inspector setter");
            editor.Duplicate(); Assert(editor.Scene.Objects.Count == count + 2, "Duplicate"); editor.DeleteSelected(); Assert(editor.Scene.Objects.Count == count + 1, "Delete"); editor.Undo(); Assert(editor.Scene.Objects.Count == count + 2, "Undo delete"); editor.Redo(); Assert(editor.Scene.Objects.Count == count + 1, "Redo delete");
            Log("EDITOR history passed"); editor.SaveScene(); Log("EDITOR saved"); var snapshot = JsonIO.Serialize(editor.Scene); editor.StartPlay(); Log("EDITOR playing"); Assert(editor.World != null, "Play did not start"); editor.World!.Scene.Objects[0].Name = "runtime-only"; editor.World.Update(.016f); editor.StopPlay(); Assert(JsonIO.Serialize(editor.Scene) == snapshot, "Stop mutated edit scene");
            editor.SelectObject(editor.Scene.FindByName("スピリットボール")); editor.RefreshHierarchy(); Application.DoEvents(); editor.TickFrame();
            Log("EDITOR capturing"); using var image = new Bitmap(editor.Width, editor.Height); editor.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
            using (var g = Graphics.FromImage(image)) using (var gpu = editor.Renderer!.Capture().ToBitmap()) { var point = editor.Viewport.PointToScreen(Point.Empty); g.DrawImageUnscaled(gpu, point.X - editor.Left, point.Y - editor.Top); }
            image.Save(Path.Combine(review, "editor-preview.png")); editor.MarkCleanForTest(); editor.Close();
        });
        Test("Delivery project templates exist and validate", () => { var delivery = new EngineProject(root); Templates.EnsureProject(delivery); foreach (var path in delivery.SceneFiles()) delivery.LoadScene(path); Assert(delivery.SceneFiles().Count() >= 4, "Missing template scenes"); });
        Log($"RESULT: {passed} passed, {failed} failed"); return failed == 0 ? 0 : 1;
    }
    static byte[] PsdFixture(ushort compression)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void U16(ushort value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
        void U32(uint value) { U16((ushort)(value >> 16)); U16((ushort)value); }
        writer.Write(Encoding.ASCII.GetBytes("8BPS")); U16(1); writer.Write(new byte[6]); U16(4); U32(1); U32(2); U16(8); U16(3); U32(0); U32(0); U32(0); U16(compression);
        byte[] channels = [255, 0, 0, 255, 0, 0, 255, 128];
        if (compression == 0) writer.Write(channels);
        else if (compression == 1) { for (int i = 0; i < 4; i++) U16(3); for (int i = 0; i < 4; i++) { writer.Write((byte)1); writer.Write(channels, i * 2, 2); } }
        else { if (compression == 3) for (int i = 0; i < 4; i++) channels[i * 2 + 1] = unchecked((byte)(channels[i * 2 + 1] - channels[i * 2])); using var zlib = new ZLibStream(stream, CompressionLevel.SmallestSize, true); zlib.Write(channels); }
        return stream.ToArray();
    }
}
