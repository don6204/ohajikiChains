using System.Diagnostics;

namespace Ohajiki.Engine;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--self-test")) Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        int projectIndex = Array.IndexOf(args, "--project"); if (projectIndex >= 0 && projectIndex + 1 < args.Length) root = Path.GetFullPath(args[projectIndex + 1]);
        try
        {
            if (args.Contains("--data-check")) { var data=GameData.Load(new EngineProject(root)); Directory.CreateDirectory(Path.Combine(root,"Review")); File.WriteAllText(Path.Combine(root,"Review","master-import.txt"),GameData.Describe(data)); return 0; }
            if (args.Contains("--install-game")) {GameData.InstallScene(new EngineProject(root));return 0;}
            if (args.Contains("--game-test")) return GameTests.Run(root,args.Contains("--graphics"));
            if (args.Contains("--save-test")) return SaveTests.Run(root,args.Contains("--graphics"));
            int probeIndex=Array.IndexOf(args,"--save-probe");
            if(probeIndex>=0)return SaveTests.Probe(root,args[probeIndex+1],args[probeIndex+2]);
            int masterIndex=Array.IndexOf(args,"--import-master");
            if(masterIndex>=0){if(masterIndex+1>=args.Length)throw new ArgumentException("MasterData.xlsxのパスが必要です");var data=GameData.ImportWorkbook(new EngineProject(root),args[masterIndex+1]);Directory.CreateDirectory(Path.Combine(root,"Review"));File.WriteAllText(Path.Combine(root,"Review","master-import.txt"),GameData.Describe(data));return 0;}
            if (args.Contains("--self-test")) return EngineTests.Run(root);
            var project = new EngineProject(root);
            if (!args.Contains("--player")) Templates.EnsureProject(project);
            if (args.Contains("--player")) Application.Run(new PlayerForm(project));
            else Application.Run(new EditorForm(project));
            return 0;
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(Path.Combine(root, "Review")); File.WriteAllText(Path.Combine(root, "Review", "startup-error.txt"), ex.ToString());
            if (!args.Any(a=>new[]{"--self-test","--data-check","--game-test","--save-test","--save-probe","--import-master","--install-game"}.Contains(a))) MessageBox.Show(ex.Message + "\n\n詳細：Review/startup-error.txt", "おはじき工房 — 起動エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

public sealed class PlayerForm : Form
{
    readonly ViewportPanel viewport = new() { Dock = DockStyle.Fill };
    readonly EngineProject project;
    readonly ScriptCompiler scripts = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly Stopwatch time = Stopwatch.StartNew();
    readonly HashSet<string> reported = new();
    D3D12Renderer? renderer;
    RuntimeWorld? world;
    double previous;
    public PlayerForm(EngineProject project)
    {
        this.project = project; Text = project.Settings.Name; var scene = project.LoadScene(project.Settings.StartScene);
        ClientSize = new Size(Math.Clamp(scene.Width, 400, 1400), Math.Clamp(scene.Height, 300, 900)); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true; Controls.Add(viewport);
        void Log(string message) { if (reported.Add(message)) { Directory.CreateDirectory(project.Resolve("Library")); File.AppendAllText(project.Resolve("Library/player.log"), DateTime.Now.ToString("O") + " " + message + "\n"); } }
        Shown += (_, _) =>
        {
            try { if (!scripts.Compile(project)) throw new InvalidOperationException(scripts.Diagnostics); renderer = new(viewport.Handle, viewport.Width, viewport.Height); world = new(project, scene, scripts, Log); previous = time.Elapsed.TotalSeconds; timer.Start(); }
            catch (Exception ex) { Log(ex.ToString()); MessageBox.Show(this, ex.Message, "起動エラー"); Close(); }
        };
        timer.Tick += (_, _) =>
        {
            try { if (world == null || renderer == null) return; double now = time.Elapsed.TotalSeconds; world.Update((float)(now - previous)); previous = now; if (WindowState != FormWindowState.Minimized) renderer.Render(SceneRenderer.Build(world.Scene, project, renderer, null, false, world, Log), world.Scene.Width, world.Scene.Height); }
            catch (Exception ex) { timer.Stop(); Log(ex.ToString()); MessageBox.Show(this, ex.Message, "実行エラー"); Close(); }
        };
        viewport.Resize += (_, _) => { try { renderer?.Resize(viewport.Width, viewport.Height); } catch (Exception ex) { timer.Stop(); Log(ex.ToString()); } };
        System.Numerics.Vector2 Point(Point p) { var s = world!.Scene; float scale = Math.Min(viewport.Width / (float)s.Width, viewport.Height / (float)s.Height); return new((p.X - (viewport.Width - s.Width * scale) / 2) / scale, (p.Y - (viewport.Height - s.Height * scale) / 2) / scale); }
        viewport.MouseDown += (_, e) => { if (world == null || e.Button != MouseButtons.Left) return; viewport.Focus(); viewport.Capture = true; world.Input.Press(Point(e.Location)); };
        viewport.MouseMove += (_, e) => { if (world != null) world.Input.Pointer = Point(e.Location); };
        viewport.MouseWheel += (_, e) => { if(world!=null)world.Input.Wheel+=e.Delta; };
        viewport.MouseUp += (_, e) => { if (world == null || e.Button != MouseButtons.Left) return; world.Input.Pointer = Point(e.Location); world.Input.Held = false; world.Input.Released = true; viewport.Capture = false; };
        KeyDown += (_, e) => { world?.Input.SetKey(e.KeyCode, true); if (e.KeyCode == Keys.Escape) {if(world?.Game is {} game)game.Activate(game.ModalOpen?"modal.cancel":"battle.pause");else Close();} }; KeyUp += (_, e) => world?.Input.SetKey(e.KeyCode, false); Deactivate += (_, _) => world?.Input.Clear();
        FormClosing += (_, e) => {if(world?.Game is {} game&&!game.TrySaveProgress()){e.Cancel=true;MessageBox.Show(this,"進行を保存できません。空き容量・書き込み権限を確認してから、終了を再試行してください。\n\n"+game.SaveError,"保存エラー");}};
        FormClosed += (_, _) => { timer.Stop(); world?.Dispose(); renderer?.Dispose(); scripts.Dispose(); timer.Dispose(); };
    }
}
