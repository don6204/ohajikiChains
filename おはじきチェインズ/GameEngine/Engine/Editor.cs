using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Ohajiki.Engine;

public sealed class ViewportPanel : Panel
{
    public ViewportPanel() { SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint, true); TabStop = true; BackColor = Color.FromArgb(19, 22, 26); }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }
}

public sealed class EditorForm : Form
{
    internal readonly EngineProject Project;
    internal SceneData Scene;
    internal string ScenePath;
    internal GameObject? Selected;
    internal D3D12Renderer? Renderer;
    internal RuntimeWorld? World;
    internal readonly ViewportPanel Viewport = new() { Dock = DockStyle.Fill, AllowDrop = true };
    internal readonly ScriptCompiler Compiler = new();
    readonly EditHistory history = new();
    readonly TreeView hierarchy = new() { Dock = DockStyle.Fill, HideSelection = false, LabelEdit = true, AllowDrop = true, BorderStyle = BorderStyle.None };
    readonly PropertyGrid inspector = new() { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized, HelpVisible = true };
    readonly TreeView folders = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None };
    readonly ListView assets = new() { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = true, AllowDrop = true, BorderStyle = BorderStyle.None };
    readonly TextBox assetFilter = new() { Width = 160, PlaceholderText = "アセット名を検索" };
    readonly RichTextBox code = new() { Dock = DockStyle.Fill, AcceptsTab = true, WordWrap = false, Font = new Font("Consolas", 11), BorderStyle = BorderStyle.None, DetectUrls = false };
    readonly RichTextBox console = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Yu Gothic UI", 9), BorderStyle = BorderStyle.None, DetectUrls = false };
    readonly Label inspectorTitle = new() { Dock = DockStyle.Top, Height = 34, Text = "インスペクター", Padding = new Padding(10, 7, 0, 0) };
    readonly Label sceneTitle = new() { AutoSize = true, Margin = new Padding(12, 7, 12, 0) };
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel graphicsStatus = new();
    readonly ToolStripButton playButton = new("▶ 再生  F5"), pauseButton = new("一時停止"), stopButton = new("■ 停止");
    readonly ToolStripComboBox transformMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    readonly ToolStripComboBox aspect = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly ToolStripButton gridButton = new("グリッド") { CheckOnClick = true, Checked = true };
    readonly ToolStripButton snapButton = new("10単位スナップ") { CheckOnClick = true };
    readonly ToolStripComboBox removeComponent = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185 };
    readonly TabControl bottomTabs = new() { Dock = DockStyle.Fill };
    readonly TabPage assetsTab = new("アセット"), codeTab = new("C#スクリプト"), consoleTab = new("コンソール");
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly Stopwatch stopwatch = Stopwatch.StartNew();
    readonly HashSet<string> seenErrors = new();
    string savedScene = "", currentFolder = "Assets", codePath = "", savedCode = "";
    bool loading, paused, closing, renderingFailed;
    double previousTime, lastAutosave, lastInspectorRefresh;
    Vector2 dragStart, originalPosition;
    float originalRotation, originalScaleX, originalScaleY;
    bool dragging;
    const string TitleBase = "おはじき工房  |  DirectX 12";
    static readonly Color Bg = Color.FromArgb(28, 37, 42), PanelBg = Color.FromArgb(35, 46, 52), Ink = Color.FromArgb(231, 234, 229), Accent = Color.FromArgb(225, 187, 106);
    public EditorForm(EngineProject project, string? scenePath = null)
    {
        Project = project; ScenePath = scenePath ?? project.Settings.LastScene;
        Scene = project.LoadScene(ScenePath); savedScene = JsonIO.Serialize(Scene);
        Text = TitleBase; Font = new Font("Yu Gothic UI", 9.5f); StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(1500, 950); MinimumSize = new Size(1120, 740); BackColor = Bg; ForeColor = Ink; KeyPreview = true;
        BuildUi(); WireEvents(); RefreshHierarchy(); RefreshFolders(); UpdateTitle();
        Shown += (_, _) =>
        {
            try { Renderer = new D3D12Renderer(Viewport.Handle, Viewport.Width, Viewport.Height, Environment.GetCommandLineArgs().Contains("--debug-gpu")); graphicsStatus.Text = "DirectX 12  •  " + Renderer.Adapter; Log("DirectX 12初期化成功：" + Renderer.Adapter); }
            catch (Exception ex) { renderingFailed = true; Log(ex.ToString()); MessageBox.Show(this, ex.Message, "描画初期化エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            CompileScripts(false); previousTime = stopwatch.Elapsed.TotalSeconds; timer.Start();
            var recovery = Project.Resolve(ScenePath + ".autosave");
            if (File.Exists(recovery) && File.GetLastWriteTimeUtc(recovery) > File.GetLastWriteTimeUtc(Project.Resolve(ScenePath)) && MessageBox.Show(this, "前回の自動保存が見つかりました。復元しますか？", "自動保存の復元", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Guard(() => { var recovered = JsonIO.Deserialize<SceneData>(File.ReadAllText(recovery)); recovered.Validate(); history.Record(Scene); Scene = recovered; RefreshHierarchy(); Changed(); });
        };
    }
    void BuildUi()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Bg, ForeColor = Ink };
        ToolStripMenuItem Menu(string label) { var item = new ToolStripMenuItem(label); menu.Items.Add(item); return item; }
        void Item(ToolStripMenuItem parent, string name, Action action, Keys shortcut = Keys.None) { var item = new ToolStripMenuItem(name, null, (_, _) => Guard(action)) { ShortcutKeys = shortcut }; parent.DropDownItems.Add(item); }
        var file = Menu("ファイル"); Item(file, "新しいシーン", NewScene, Keys.Control | Keys.N); Item(file, "シーンを開く…", OpenSceneDialog, Keys.Control | Keys.O); Item(file, "保存", () => { if (code.ContainsFocus) SaveCode(); else SaveScene(); }, Keys.Control | Keys.S); Item(file, "名前を付けて保存…", SaveSceneAs, Keys.Control | Keys.Shift | Keys.S); file.DropDownItems.Add(new ToolStripSeparator()); Item(file, "Windowsプレイヤーを書き出す…", ExportPlayer); Item(file, "終了", Close);
        var edit = Menu("編集"); Item(edit, "元に戻す", Undo, Keys.Control | Keys.Z); Item(edit, "やり直す", Redo, Keys.Control | Keys.Y); Item(edit, "複製", Duplicate, Keys.Control | Keys.D); Item(edit, "削除", DeleteSelected);
        var objects = Menu("オブジェクト"); foreach (var type in new[] { "空のオブジェクト", "スプライト", "テキスト", "ボタン", "スピリットボール", "盤面", "エフェクト" }) { var choice = type; Item(objects, choice, () => CreateObject(choice)); } Item(objects, "選択オブジェクトの親を解除", Unparent); Item(objects, "プレハブとして保存…", SavePrefab);
        var scenes = Menu("シーン"); Item(scenes, "画面サイズ・遷移を設定…", SceneSettings); Item(scenes, "このシーンを起動シーンにする", () => { Project.Settings.StartScene = ScenePath; Project.SaveSettings(); Log("起動シーン：" + ScenePath); }); Item(scenes, "選択した盤面にボールを生成", GenerateBoard);
        var assetMenu = Menu("アセット"); Item(assetMenu, "ファイルを取り込む…", ImportFiles); Item(assetMenu, "フォルダを取り込む…", ImportFolder); Item(assetMenu, "フォルダを作成…", NewFolder); Item(assetMenu, "C#スクリプトを作成…", NewScript); Item(assetMenu, "C#をコンパイル", () => CompileScripts(true), Keys.Control | Keys.B); Item(assetMenu, "表示を更新", RefreshFolders);
        var run = Menu("実行"); Item(run, "再生 / 停止", TogglePlay, Keys.F5); Item(run, "一時停止 / 再開", TogglePause, Keys.F6); Item(run, "1フレーム進める", Step, Keys.F7);
        var gameMenu=Menu("ゲーム");
        Item(gameMenu,"おはじきチェインズを開く",()=>OpenScene("Assets/Scenes/OhajikiChains.scene.json"));
        Item(gameMenu,"MasterDataを検証",()=>{if(World!=null)throw new InvalidOperationException("再生を停止してから実行してください。");var data=GameData.Load(Project);Log(GameData.Describe(data));MessageBox.Show(this,GameData.Describe(data),"MasterData検証完了");});
        Item(gameMenu,"MasterDataを取り込む…",()=>{if(World!=null)throw new InvalidOperationException("再生を停止してから実行してください。");using var dialog=new OpenFileDialog{Filter="MasterData (*.xlsx)|*.xlsx",InitialDirectory=Path.GetDirectoryName(Project.Root),FileName="MasterData.xlsx"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;var data=GameData.ImportWorkbook(Project,dialog.FileName);Log("MasterData更新："+GameData.Describe(data));RefreshFolders();MessageBox.Show(this,"MasterDataを更新しました。次の再生から反映します。\n\n"+GameData.Describe(data),"取り込み完了");});
        var help = Menu("ヘルプ"); Item(help, "使い方", ShowHelp); Item(help, "操作のヒント", () => MessageBox.Show(this, "オブジェクトを追加 → インスペクターで調整 → 再生で確認\n\nW：移動 / E：回転 / R：拡大縮小\nドラッグ：現在のツールで操作\nCtrl+D：複製 / Delete：削除\nCtrl+Z / Ctrl+Y：元に戻す・やり直す\n\n画像をアセットへドロップして取り込み。\nアセット画像を画面へドロップして配置。\n階層内のドラッグで親子関係を変更。\n\n停止すると再生前の編集状態に戻ります。", "操作のヒント"));
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(8, 5, 8, 5), Height = 42 };
        AddButton(toolbar, "保存", SaveScene); toolbar.Items.Add(new ToolStripSeparator()); toolbar.Items.Add(playButton); toolbar.Items.Add(pauseButton); toolbar.Items.Add(stopButton); AddButton(toolbar, "1フレーム", Step); toolbar.Items.Add(new ToolStripSeparator());
        transformMode.Items.AddRange(["移動  W", "回転  E", "拡大縮小  R"]); transformMode.SelectedIndex = 0; toolbar.Items.Add(transformMode); toolbar.Items.Add(snapButton); toolbar.Items.Add(gridButton); toolbar.Items.Add(new ToolStripSeparator());
        aspect.Items.AddRange(["9 : 16", "16 : 9", "1 : 1", "4 : 3", "自由設定…"]); aspect.SelectedIndex = -1; aspect.Text = "画面比率"; toolbar.Items.Add(new ToolStripLabel("画面比率")); toolbar.Items.Add(aspect); AddButton(toolbar, "シーン設定", SceneSettings);
        var statusBar = new StatusStrip { BackColor = Bg, ForeColor = Ink }; statusBar.Items.Add(status); statusBar.Items.Add(graphicsStatus);
        var workspace = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, FixedPanel = FixedPanel.Panel2 };
        var leftSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1 };
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel2 };
        var leftHeader = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden }; leftHeader.Items.Add(new ToolStripLabel("階層")); var add = new ToolStripDropDownButton("＋ 追加"); foreach (var type in new[] { "空のオブジェクト", "スプライト", "テキスト", "ボタン", "スピリットボール", "盤面", "エフェクト" }) { var choice = type; add.DropDownItems.Add(choice, null, (_, _) => Guard(() => CreateObject(choice))); } leftHeader.Items.Add(add);
        AddButton(leftHeader, "複製", Duplicate); leftSplit.Panel1.Controls.Add(hierarchy); leftSplit.Panel1.Controls.Add(leftHeader);
        var viewHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, BackColor = PanelBg }; viewHeader.Controls.Add(sceneTitle); rightSplit.Panel1.Controls.Add(Viewport); rightSplit.Panel1.Controls.Add(viewHeader);
        var componentBar = new ToolStrip { Dock = DockStyle.Bottom, GripStyle = ToolStripGripStyle.Hidden }; var addComponent = new ToolStripDropDownButton("＋ コンポーネント"); foreach (var def in ComponentsCatalog.All) { var key = def.Key; addComponent.DropDownItems.Add(def.Value.Label, null, (_, _) => Guard(() => AddComponent(key))); } componentBar.Items.Add(addComponent);
        var removeBar = new ToolStrip { Dock = DockStyle.Bottom, GripStyle = ToolStripGripStyle.Hidden }; removeBar.Items.Add(removeComponent); AddButton(removeBar, "削除", RemoveComponent);
        rightSplit.Panel2.Controls.Add(inspector); rightSplit.Panel2.Controls.Add(removeBar); rightSplit.Panel2.Controls.Add(componentBar); rightSplit.Panel2.Controls.Add(inspectorTitle);
        leftSplit.Panel2.Controls.Add(rightSplit); workspace.Panel1.Controls.Add(leftSplit); workspace.Panel2.Controls.Add(bottomTabs);
        var assetSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5, FixedPanel = FixedPanel.Panel1 }; assetSplit.Panel1.Controls.Add(folders); assetSplit.Panel2.Controls.Add(assets);
        var assetTools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden }; AddButton(assetTools, "取り込み", ImportFiles); AddButton(assetTools, "フォルダ＋", NewFolder); AddButton(assetTools, "C#作成", NewScript); AddButton(assetTools, "更新", RefreshFolders); assetTools.Items.Add(new ToolStripControlHost(assetFilter));
        assetsTab.Controls.Add(assetSplit); assetsTab.Controls.Add(assetTools);
        var codeTools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden }; AddButton(codeTools, "スクリプトを保存", SaveCode); AddButton(codeTools, "保存してコンパイル", () => { SaveCode(); CompileScripts(true); }); AddButton(codeTools, "選択物に追加", AttachCurrentScript); codeTab.Controls.Add(code); codeTab.Controls.Add(codeTools);
        var consoleTools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden }; AddButton(consoleTools, "消去", () => { console.Clear(); seenErrors.Clear(); }); consoleTab.Controls.Add(console); consoleTab.Controls.Add(consoleTools);
        bottomTabs.TabPages.AddRange([assetsTab, codeTab, consoleTab]);
        Controls.Add(workspace); Controls.Add(toolbar); Controls.Add(menu); Controls.Add(statusBar); MainMenuStrip = menu;
        ApplyTheme(this);
        Load += (_, _) => { workspace.SplitterDistance = Math.Max(430, workspace.Height - 238); leftSplit.SplitterDistance = 238; rightSplit.SplitterDistance = Math.Max(400, rightSplit.Width - 340); assetSplit.SplitterDistance = 215; };
        pauseButton.Enabled = stopButton.Enabled = false;
    }
    static void ApplyTheme(Control control)
    {
        if (control is not ViewportPanel) { control.BackColor = control is TextBoxBase or TreeView or ListView or PropertyGrid ? PanelBg : Bg; control.ForeColor = Ink; }
        if (control is PropertyGrid p) { p.ViewBackColor = PanelBg; p.ViewForeColor = Ink; p.HelpBackColor = Bg; p.HelpForeColor = Ink; p.CategoryForeColor = Accent; p.CategorySplitterColor = Bg; p.LineColor = Bg; p.ViewBorderColor = Bg; p.HelpBorderColor = Bg; }
        if (control is ToolStrip strip) { strip.Renderer = new ToolStripProfessionalRenderer(new EngineColors()); void StyleItem(ToolStripItem i) { i.ForeColor = Ink; if (i is ToolStripComboBox combo) { combo.BackColor = combo.ComboBox.BackColor = PanelBg; combo.ForeColor = combo.ComboBox.ForeColor = Ink; combo.ComboBox.FlatStyle = FlatStyle.Flat; } if (i is ToolStripDropDownItem drop) foreach (ToolStripItem child in drop.DropDownItems) StyleItem(child); } foreach (ToolStripItem i in strip.Items) StyleItem(i); }
        foreach (Control child in control.Controls) ApplyTheme(child);
    }
    sealed class EngineColors : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Bg; public override Color ToolStripGradientMiddle => Bg; public override Color ToolStripGradientEnd => Bg;
        public override Color ToolStripDropDownBackground => PanelBg; public override Color ImageMarginGradientBegin => PanelBg; public override Color ImageMarginGradientMiddle => PanelBg; public override Color ImageMarginGradientEnd => PanelBg;
        public override Color MenuItemSelected => Color.FromArgb(65, 85, 86); public override Color MenuItemBorder => Color.FromArgb(85, 108, 108); public override Color MenuItemSelectedGradientBegin => MenuItemSelected; public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color ButtonSelectedGradientBegin => MenuItemSelected; public override Color ButtonSelectedGradientMiddle => MenuItemSelected; public override Color ButtonSelectedGradientEnd => MenuItemSelected;
        public override Color ButtonCheckedGradientBegin => Color.FromArgb(64, 95, 92); public override Color ButtonCheckedGradientMiddle => ButtonCheckedGradientBegin; public override Color ButtonCheckedGradientEnd => ButtonCheckedGradientBegin;
    }
    void AddButton(ToolStrip strip, string label, Action action) => strip.Items.Add(new ToolStripButton(label, null, (_, _) => Guard(action)));
    void WireEvents()
    {
        timer.Tick += (_, _) => TickFrame(); playButton.Click += (_, _) => Guard(TogglePlay); stopButton.Click += (_, _) => Guard(StopPlay); pauseButton.Click += (_, _) => Guard(TogglePause);
        hierarchy.AfterSelect += (_, e) => { if (!loading) SelectObject(Scene.Find(e.Node?.Tag as string ?? ""), false); };
        hierarchy.BeforeLabelEdit += (_, e) => { if (World != null) e.CancelEdit = true; };
        hierarchy.AfterLabelEdit += (_, e) => { if (e.Label != null && Scene.Find(e.Node?.Tag as string ?? "") is { } obj) { history.Record(Scene); obj.Name = e.Label; Changed(); } };
        hierarchy.ItemDrag += (_, e) => { if (World == null && e.Item is TreeNode n) hierarchy.DoDragDrop("object:" + n.Tag, DragDropEffects.Move); };
        hierarchy.DragEnter += ObjectDragEnter; hierarchy.DragOver += ObjectDragEnter;
        hierarchy.DragDrop += (_, e) => Guard(() =>
        {
            if (World != null) return; var data = e.Data?.GetData(DataFormats.Text) as string; if (data?.StartsWith("object:") != true) return;
            var obj = Scene.Find(data[7..]); var node = hierarchy.GetNodeAt(hierarchy.PointToClient(new Point(e.X, e.Y))); var parent = node?.Tag is string id ? Scene.Find(id) : null;
            if (obj == null) return; history.Record(Scene); if (!Scene.Reparent(obj, parent)) { Log("親子関係を変更できません。循環参照または非一様拡大による変形を確認してください。"); return; } RefreshHierarchy(); SelectObject(obj); Changed();
        });
        folders.AfterSelect += (_, e) => { if (e.Node?.Tag is string folder) { currentFolder = folder; RefreshAssets(); } };
        assetFilter.TextChanged += (_, _) => RefreshAssets();
        assets.DoubleClick += (_, _) => Guard(OpenAsset);
        assets.ItemDrag += (_, _) => { if (assets.SelectedItems.Count > 0) assets.DoDragDrop("asset:" + assets.SelectedItems[0].Tag, DragDropEffects.Copy); };
        assets.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        assets.DragDrop += (_, e) => Guard(() => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) ImportPaths(paths); });
        Viewport.DragEnter += (_, e) => { if (World == null && (e.Data?.GetDataPresent(DataFormats.FileDrop) == true || (e.Data?.GetData(DataFormats.Text) as string)?.StartsWith("asset:") == true)) e.Effect = DragDropEffects.Copy; };
        Viewport.DragDrop += (_, e) => Guard(() =>
        {
            if (World != null) return; var point = ScreenToScene(Viewport.PointToClient(new Point(e.X, e.Y)));
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) foreach (var path in paths) PlaceAsset(AssetIO.Import(Project, path, currentFolder), point);
            else if (e.Data?.GetData(DataFormats.Text) is string text && text.StartsWith("asset:")) PlaceAsset(text[6..], point); RefreshFolders();
        });
        Viewport.MouseDown += ViewMouseDown; Viewport.MouseMove += ViewMouseMove; Viewport.MouseUp += ViewMouseUp;
        Viewport.MouseWheel += (_,e)=>{if(World!=null)World.Input.Wheel+=e.Delta;};
        Viewport.MouseCaptureChanged += (_, _) => { if (!Viewport.Capture) dragging = false; };
        Viewport.Resize += (_, _) => { if (Renderer != null && !renderingFailed) Guard(() => Renderer.Resize(Viewport.Width, Viewport.Height)); };
        aspect.SelectedIndexChanged += (_, _) => Guard(() =>
        {
            if (loading || World != null) return; if (aspect.SelectedIndex == 4) { SceneSettings(); return; } if (aspect.SelectedIndex < 0) return;
            history.Record(Scene); var dimensions = new[] { (720, 1280), (1280, 720), (1000, 1000), (1200, 900) }[aspect.SelectedIndex]; Scene.Width = dimensions.Item1; Scene.Height = dimensions.Item2; Changed();
        });
        code.TextChanged += (_, _) => { if (!loading) codeTab.Text = "C#スクリプト" + (code.Text != savedCode ? " *" : ""); };
        KeyDown += EditorKeyDown; KeyUp += (_, e) => World?.Input.SetKey(e.KeyCode, false);
        Deactivate += (_, _) => { World?.Input.Clear(); dragging = false; };
        FormClosing += EditorClosing;
    }
    void ObjectDragEnter(object? sender, DragEventArgs e) { if (World == null && (e.Data?.GetData(DataFormats.Text) as string)?.StartsWith("object:") == true) e.Effect = DragDropEffects.Move; }
    void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (World != null) { World.Input.SetKey(e.KeyCode, true); return; }
        if (code.ContainsFocus || inspector.ContainsFocus || assetFilter.ContainsFocus || hierarchy.IsEditing()) return;
        if (e.KeyCode == Keys.Delete) { Guard(DeleteSelected); e.Handled = true; }
        if (e.Modifiers == Keys.None && e.KeyCode is Keys.W or Keys.E or Keys.R) { transformMode.SelectedIndex = e.KeyCode == Keys.W ? 0 : e.KeyCode == Keys.E ? 1 : 2; e.Handled = true; }
    }
    internal void Log(string message)
    {
        if (console.TextLength > 150000) console.Text = console.Text[^75000..];
        console.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n"); console.SelectionStart = console.TextLength; console.ScrollToCaret(); status.Text = message.Split('\n')[0];
    }
    void Guard(Action action) { try { action(); } catch (Exception ex) { Log(ex.ToString()); MessageBox.Show(this, ex.Message, "操作を完了できませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    internal void TickFrame()
    {
        if (closing) return; var now = stopwatch.Elapsed.TotalSeconds; var dt = (float)Math.Min(.1, now - previousTime); previousTime = now;
        try
        {
            if (World != null && !paused) World.Update(dt);
            var display = World?.Scene ?? Scene;
            if (Renderer != null && !renderingFailed && Viewport.Width > 0 && Viewport.Height > 0 && WindowState != FormWindowState.Minimized)
            {
                var draws = SceneRenderer.Build(display, Project, Renderer, World == null ? Selected : null, World == null && gridButton.Checked, World, error => { if (seenErrors.Add(error)) Log(error); });
                Renderer.Render(draws, display.Width, display.Height);
            }
            if (now - lastAutosave >= 45 && World == null) { lastAutosave = now; if (Dirty) { JsonIO.Write(Project.Resolve(ScenePath + ".autosave"), Scene); status.Text = "自動保存しました（通常保存は Ctrl+S）"; } }
            if (dragging && now - lastInspectorRefresh > .15) { lastInspectorRefresh = now; inspector.Refresh(); }
        }
        catch (Exception ex) { timer.Stop(); Log(ex.ToString()); renderingFailed = true; status.Text = "実行エラー。コンソールを確認し、エディターを再起動してください。"; bottomTabs.SelectedTab = consoleTab; }
    }
    bool Dirty => savedScene != JsonIO.Serialize(Scene);
    void Changed() { UpdateTitle(); inspector.Refresh(); if (Selected != null) foreach (TreeNode root in hierarchy.Nodes) UpdateNode(root); }
    void UpdateNode(TreeNode node) { if (node.Tag is string id && Scene.Find(id) is { } obj) node.Text = (obj.Active ? "" : "（無効）") + obj.Name; foreach (TreeNode child in node.Nodes) UpdateNode(child); }
    void UpdateTitle() { Text = $"{Scene.Name}{(Dirty ? " *" : "")}  —  {TitleBase}"; sceneTitle.Text = $"{Scene.Name}     {Scene.Width} × {Scene.Height}"; }
    internal void RefreshHierarchy()
    {
        loading = true; hierarchy.BeginUpdate(); var expanded = new HashSet<string>(); void Collect(TreeNode n) { if (n.IsExpanded && n.Tag is string id) expanded.Add(id); foreach (TreeNode c in n.Nodes) Collect(c); } foreach (TreeNode n in hierarchy.Nodes) Collect(n);
        hierarchy.Nodes.Clear(); var nodes = Scene.Objects.ToDictionary(o => o.Id, o => new TreeNode((o.Active ? "" : "（無効）") + o.Name) { Tag = o.Id });
        foreach (var obj in Scene.Objects) { var node = nodes[obj.Id]; if (obj.ParentId != null && nodes.TryGetValue(obj.ParentId, out var parent)) parent.Nodes.Add(node); else hierarchy.Nodes.Add(node); }
        foreach (var id in expanded) if (nodes.TryGetValue(id, out var n)) n.Expand();
        if (Selected != null && nodes.TryGetValue(Selected.Id, out var selectedNode)) hierarchy.SelectedNode = selectedNode;
        hierarchy.EndUpdate(); loading = false;
    }
    internal void SelectObject(GameObject? obj, bool selectTree = true)
    {
        Selected = obj; inspectorTitle.Text = obj == null ? "インスペクター" : obj.Name;
        inspector.SelectedObject = obj == null ? null : new InspectorObject(obj, Scene, Compiler, () => { if (World == null) history.Record(Scene); }, Changed);
        removeComponent.Items.Clear(); if (obj != null) foreach (var c in obj.Components) removeComponent.Items.Add(ComponentsCatalog.All.GetValueOrDefault(c.Type)?.Label ?? c.Type); if (removeComponent.Items.Count > 0) removeComponent.SelectedIndex = 0;
        if (obj != null && selectTree)
        {
            TreeNode? Find(TreeNodeCollection list) { foreach (TreeNode n in list) { if ((string?)n.Tag == obj.Id) return n; var found = Find(n.Nodes); if (found != null) return found; } return null; }
            loading = true; hierarchy.SelectedNode = Find(hierarchy.Nodes); hierarchy.SelectedNode?.EnsureVisible(); loading = false;
        }
    }
    internal void CreateObject(string type)
    {
        if (World != null) return; history.Record(Scene); float x = Scene.Width / 2f, y = Scene.Height / 2f; GameObject obj;
        switch (type)
        {
            case "スプライト": obj = Templates.Sprite(Scene, "スプライト", x, y, 120, 120, "#78C5B9"); break;
            case "テキスト": obj = Templates.Text(Scene, "テキスト", "新しいテキスト", x, y, 340, 80, 32); break;
            case "ボタン": obj = Templates.Sprite(Scene, "ボタン", x, y, 280, 90, "#387D77"); obj.AddComponent("Button"); obj.AddComponent("Text").Set("Text", "ボタン"); break;
            case "スピリットボール": obj = Templates.Sprite(Scene, "スピリットボール", x, y, 52, 52, "#FFDC94", "円"); obj.AddComponent("Spirit"); obj.AddComponent("BoxCollider"); obj.AddComponent("RigidBody"); obj.AddComponent("Shot").Set("LaunchY", y); break;
            case "盤面": obj = Templates.Sprite(Scene, "盤面", x, y, 624, 850, "#22424D"); obj.AddComponent("Board"); break;
            case "エフェクト": obj = Scene.CreateObject("エフェクト"); obj.Transform.X = x; obj.Transform.Y = y; obj.AddComponent("ParticleEmitter"); break;
            default: obj = Scene.CreateObject("空のオブジェクト"); obj.Transform.X = x; obj.Transform.Y = y; break;
        }
        RefreshHierarchy(); SelectObject(obj); Changed();
    }
    void AddComponent(string type) { if (World != null || Selected == null) return; history.Record(Scene); var c = Selected.AddComponent(type); if (type == "Script" && Compiler.Types.Count > 0) c.Set("Class", Compiler.Types.Keys.First()); SelectObject(Selected); Changed(); }
    void RemoveComponent() { if (World != null || Selected == null || removeComponent.SelectedIndex < 0) return; history.Record(Scene); Selected.Components.RemoveAt(removeComponent.SelectedIndex); SelectObject(Selected); Changed(); }
    internal void DeleteSelected() { if (World != null || Selected == null) return; history.Record(Scene); Scene.Remove(Selected); SelectObject(null); RefreshHierarchy(); Changed(); }
    internal void Duplicate() { if (World != null || Selected == null) return; history.Record(Scene); var copy = Scene.Duplicate(Selected); RefreshHierarchy(); SelectObject(copy); Changed(); }
    void Unparent() { if (World != null || Selected == null) return; history.Record(Scene); if (!Scene.Reparent(Selected, null)) throw new InvalidOperationException("親を解除すると変形が生じます。親の拡大率を揃えてください。"); RefreshHierarchy(); Changed(); }
    void GenerateBoard()
    {
        if (World != null || Selected?.GetComponent("Board") == null) { Log("盤面コンポーネントのあるオブジェクトを選択してください。"); return; }
        var original = JsonIO.Clone(Scene); history.Record(Scene); try { BoardTools.Generate(Scene, Selected); } catch { Scene = original; SelectObject(Scene.Find(Selected.Id)); RefreshHierarchy(); throw; } RefreshHierarchy(); Changed();
    }
    internal void Undo() { if (World != null || code.ContainsFocus) { if (code.ContainsFocus) code.Undo(); return; } var id = Selected?.Id; Scene = history.Undo(Scene); SelectObject(id == null ? null : Scene.Find(id)); RefreshHierarchy(); Changed(); }
    internal void Redo() { if (World != null || code.ContainsFocus) { if (code.ContainsFocus) code.Redo(); return; } var id = Selected?.Id; Scene = history.Redo(Scene); SelectObject(id == null ? null : Scene.Find(id)); RefreshHierarchy(); Changed(); }
    internal Vector2 ScreenToScene(Point point)
    {
        var s = World?.Scene ?? Scene; float scale = Math.Min(Viewport.Width / (float)s.Width, Viewport.Height / (float)s.Height); if (scale <= 0) return Vector2.Zero;
        return new((point.X - (Viewport.Width - s.Width * scale) / 2) / scale, (point.Y - (Viewport.Height - s.Height * scale) / 2) / scale);
    }
    void ViewMouseDown(object? sender, MouseEventArgs e)
    {
        Viewport.Focus(); if (e.Button != MouseButtons.Left) return; var point = ScreenToScene(e.Location);
        if (World != null) { if (!paused) { World.Input.Press(point); Viewport.Capture = true; } return; }
        var selected = SceneRenderer.Pick(Scene, point); SelectObject(selected); if (selected == null) return;
        history.Record(Scene); dragStart = point; originalPosition = Scene.Position(selected); originalRotation = selected.Transform.Rotation; originalScaleX = selected.Transform.ScaleX; originalScaleY = selected.Transform.ScaleY; dragging = true; Viewport.Capture = true;
    }
    void ViewMouseMove(object? sender, MouseEventArgs e)
    {
        var point = ScreenToScene(e.Location); if (World != null) { World.Input.Pointer = point; return; } if (!dragging || Selected == null) return;
        var delta = point - dragStart;
        if (transformMode.SelectedIndex == 0) { var p = originalPosition + delta; if (snapButton.Checked) { p.X = MathF.Round(p.X / 10) * 10; p.Y = MathF.Round(p.Y / 10) * 10; } Scene.SetWorldPosition(Selected, p); }
        else if (transformMode.SelectedIndex == 1)
        {
            var start = dragStart - originalPosition; var end = point - originalPosition; float angle = start.Length() > 5 ? (MathF.Atan2(end.Y, end.X) - MathF.Atan2(start.Y, start.X)) * 180 / MathF.PI : delta.X;
            Selected.Transform.Rotation = originalRotation + angle; if (snapButton.Checked) Selected.Transform.Rotation = MathF.Round(Selected.Transform.Rotation / 15) * 15;
        }
        else { float factor = MathF.Max(.02f, 1 + (delta.X - delta.Y) / 160); if (snapButton.Checked) factor = MathF.Max(.1f, MathF.Round(factor * 10) / 10); Selected.Transform.ScaleX = originalScaleX * factor; Selected.Transform.ScaleY = originalScaleY * factor; }
        UpdateTitle();
    }
    void ViewMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return; if (World != null) { World.Input.Pointer = ScreenToScene(e.Location); World.Input.Held = false; World.Input.Released = true; } dragging = false; Viewport.Capture = false; inspector.Refresh();
    }
    bool ConfirmSave()
    {
        if (!Dirty) return true; var answer = MessageBox.Show(this, "変更したシーンを保存しますか？", "シーンの保存", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false; if (answer == DialogResult.Yes) SaveScene(); return true;
    }
    internal void SaveScene() { if (World != null) { Log("シーンを保存するには再生を停止してください。"); return; } Project.SaveScene(ScenePath, Scene); savedScene = JsonIO.Serialize(Scene); Project.Settings.LastScene = ScenePath; Project.SaveSettings(); var auto = Project.Resolve(ScenePath + ".autosave"); if (File.Exists(auto)) File.Delete(auto); UpdateTitle(); Log("シーンを保存：" + ScenePath); RefreshFolders(); }
    void SaveSceneAs()
    {
        if (World != null) return; using var dialog = new SaveFileDialog { InitialDirectory = Project.Resolve("Assets/Scenes"), Filter = "シーン (*.scene.json)|*.scene.json", FileName = Scene.Name + ".scene.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return; ScenePath = Project.Relative(dialog.FileName); SaveScene();
    }
    void NewScene()
    {
        if (World != null || !ConfirmSave()) return; var name = Ask("新しいシーン", "シーン名", "新しいシーン"); if (name == null) return; ValidName(name);
        var path = Project.Relative(AssetIO.Unique(Project.Resolve("Assets/Scenes/" + name + ".scene.json"))); Scene = new() { Name = name }; ScenePath = path; savedScene = ""; history.Clear(); SelectObject(null); RefreshHierarchy(); SaveScene();
    }
    void OpenSceneDialog()
    {
        if (World != null) return; using var dialog = new OpenFileDialog { InitialDirectory = Project.Resolve("Assets/Scenes"), Filter = "シーン (*.scene.json)|*.scene.json" }; if (dialog.ShowDialog(this) == DialogResult.OK) OpenScene(Project.Relative(dialog.FileName));
    }
    internal void OpenScene(string path)
    {
        if (World != null || !ConfirmSave()) return; var next = Project.LoadScene(path); Scene = next; ScenePath = path; savedScene = JsonIO.Serialize(Scene); history.Clear(); SelectObject(null); RefreshHierarchy(); Renderer?.ClearTextures(); seenErrors.Clear(); Project.Settings.LastScene = path; Project.SaveSettings(); UpdateTitle(); Log("シーンを開く：" + path);
    }
    internal bool CompileScripts(bool reveal)
    {
        if (World != null) { Log("コンパイル前に再生を停止してください。"); return false; } var success = Compiler.Compile(Project); Log(Compiler.Diagnostics); if (reveal || !success) bottomTabs.SelectedTab = consoleTab; SelectObject(Selected); return success;
    }
    internal void TogglePlay() { if (World != null) StopPlay(); else StartPlay(); }
    internal void StartPlay()
    {
        if (World != null || renderingFailed) return; if (!ConfirmCode()) return; Scene.Validate(); if (!CompileScripts(false)) return;
        // Resolve every attached class before any OnStart can change state.
        foreach (var o in Scene.Objects) foreach (var c in o.Components.Where(c => c.Enabled && c.Type == "Script")) if (!Compiler.Types.ContainsKey(c.Get("Class"))) throw new InvalidOperationException($"{o.Name}：C#クラス {c.Get("Class")} がありません。");
        World = new RuntimeWorld(Project, Scene, Compiler, Log); paused = false; SetPlayingUi(true); Log("再生開始。停止すると編集前の状態に戻ります。"); Viewport.Focus();
    }
    internal void StopPlay() { if(World?.Game is {} game&&!game.TrySaveProgress()){Log("進行を保存できないため再生を停止できません："+game.SaveError);return;} World?.Dispose(); World = null; paused = false; SetPlayingUi(false); Renderer?.ClearTextures(); seenErrors.Clear(); Log("再生停止。編集状態に戻りました。"); }
    void SetPlayingUi(bool playing) { playButton.Text = playing ? "再生中  F5で停止" : "▶ 再生  F5"; playButton.Checked = playing; pauseButton.Enabled = stopButton.Enabled = playing; pauseButton.Checked = false; inspector.Enabled = hierarchy.Enabled = !playing; aspect.Enabled = transformMode.Enabled = !playing; inspectorTitle.Text = playing ? "再生中（停止して編集）" : Selected?.Name ?? "インスペクター"; }
    void TogglePause() { if (World == null) return; paused = !paused; pauseButton.Checked = paused; World.Input.Clear(); Log(paused ? "一時停止" : "再開"); }
    void Step() { if (World == null) return; paused = true; pauseButton.Checked = true; World.Update(1f / 60); }
    void SceneSettings()
    {
        if (World != null) return; using var dialog = new SceneSettingsForm(Project, Scene); if (dialog.ShowDialog(this) != DialogResult.OK) return; history.Record(Scene); Scene.Name = dialog.Result.Name; Scene.Width = dialog.Result.Width; Scene.Height = dialog.Result.Height; Scene.Background = dialog.Result.Background; Scene.Transitions = dialog.Result.Transitions; Changed();
    }
    void RefreshFolders()
    {
        var previous = currentFolder; folders.BeginUpdate(); folders.Nodes.Clear();
        TreeNode Add(string path) { var node = new TreeNode(Path.GetFileName(path)) { Tag = Project.Relative(path) }; foreach (var d in Directory.EnumerateDirectories(path).OrderBy(p => p)) if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) node.Nodes.Add(Add(d)); return node; }
        var root = Add(Project.Assets); root.Text = "Assets（アセット）"; folders.Nodes.Add(root); root.Expand();
        TreeNode? Find(TreeNode n) { if ((string?)n.Tag == previous) return n; foreach (TreeNode c in n.Nodes) { var f = Find(c); if (f != null) return f; } return null; } folders.SelectedNode = Find(root) ?? root; folders.EndUpdate(); RefreshAssets();
    }
    void RefreshAssets()
    {
        if (!Directory.Exists(Project.Resolve(currentFolder))) return; assets.BeginUpdate(); assets.Items.Clear(); var old = assets.LargeImageList; var images = new ImageList { ImageSize = new Size(54, 54), ColorDepth = ColorDepth.Depth32Bit }; _ = images.Handle; assets.LargeImageList = images;
        foreach (var path in Directory.EnumerateFileSystemEntries(Project.Resolve(currentFolder)).OrderBy(p => !Directory.Exists(p)).ThenBy(p => p))
        {
            if (path.EndsWith(".bak") || path.EndsWith(".autosave") || !Path.GetFileName(path).Contains(assetFilter.Text, StringComparison.OrdinalIgnoreCase)) continue;
            using var icon = new Bitmap(54, 54); using var g = Graphics.FromImage(icon); g.Clear(PanelBg);
            try
            {
                if (AssetIO.IsTexture(path)) { var pixels = AssetIO.ReadTexture(path); using var bitmap = pixels.ToBitmap(); float scale = Math.Min(52f / bitmap.Width, 52f / bitmap.Height); g.DrawImage(bitmap, (54 - bitmap.Width * scale) / 2, (54 - bitmap.Height * scale) / 2, bitmap.Width * scale, bitmap.Height * scale); }
                else { using var font = new Font("Yu Gothic UI", 10, FontStyle.Bold); using var brush = new SolidBrush(Directory.Exists(path) ? Accent : Color.FromArgb(133, 199, 184)); var label = Directory.Exists(path) ? "フォルダ" : path.EndsWith(".cs") ? "C#" : path.EndsWith(".scene.json") ? "場面" : path.EndsWith(".prefab.json") ? "物体" : "資料"; g.DrawString(label, font, brush, 1, 16); }
            }
            catch { g.FillRectangle(Brushes.DarkRed, 5, 5, 44, 44); }
            images.Images.Add(icon); assets.Items.Add(new ListViewItem(Path.GetFileName(path), images.Images.Count - 1) { Tag = Project.Relative(path), ToolTipText = Project.Relative(path) });
        }
        old?.Dispose(); assets.ShowItemToolTips = true; assets.EndUpdate();
    }
    void OpenAsset()
    {
        if (assets.SelectedItems.Count == 0) return; var relative = (string)assets.SelectedItems[0].Tag!; var path = Project.Resolve(relative);
        if (Directory.Exists(path)) { currentFolder = relative; RefreshFolders(); }
        else if (relative.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase)) OpenScene(relative);
        else if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) OpenCode(relative);
        else if (relative.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase)) PlaceAsset(relative, new(Scene.Width / 2f, Scene.Height / 2f));
        else if (AssetIO.IsTexture(relative))
        {
            using var bitmap = AssetIO.ReadTexture(path).ToBitmap(); using var form = new Form { Text = Path.GetFileName(path), Size = new Size(650, 650), StartPosition = FormStartPosition.CenterParent, BackColor = PanelBg }; form.Controls.Add(new PictureBox { Image = bitmap, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom }); form.ShowDialog(this);
        }
    }
    internal void PlaceAsset(string relative, Vector2 point)
    {
        if (World != null) return;
        if (AssetIO.IsTexture(relative))
        {
            var pixels = AssetIO.ReadTexture(Project.Resolve(relative)); history.Record(Scene); var o = Templates.Sprite(Scene, Path.GetFileNameWithoutExtension(relative), point.X, point.Y, pixels.Width, pixels.Height, "#FFFFFF"); o.GetComponent("Sprite")!.Set("Texture", relative); var factor = Math.Min(1, 300f / Math.Max(pixels.Width, pixels.Height)); o.Transform.ScaleX = o.Transform.ScaleY = factor; RefreshHierarchy(); SelectObject(o); Changed();
        }
        else if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) { OpenCode(relative); AttachCurrentScript(); }
        else if (relative.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase))
        {
            var prefab = Project.LoadScene(relative); if (prefab.Objects.Count == 0) return; history.Record(Scene); var temp = JsonIO.Clone(prefab); var remap = temp.Objects.ToDictionary(o => o.Id, _ => Guid.NewGuid().ToString("N"));
            foreach (var o in temp.Objects) { o.Id = remap[o.Id]; if (o.ParentId != null) o.ParentId = remap[o.ParentId]; foreach (var c in o.Components) c.Id = Guid.NewGuid().ToString("N"); }
            var root = temp.Objects.First(o => o.ParentId == null); root.Transform.X = point.X; root.Transform.Y = point.Y; Scene.Objects.AddRange(temp.Objects); RefreshHierarchy(); SelectObject(root); Changed();
        }
    }
    void ImportFiles() { using var dialog = new OpenFileDialog { Filter = "アセット|*.png;*.jpg;*.jpeg;*.psd;*.cs;*.wav;*.json|すべてのファイル|*.*", Multiselect = true }; if (dialog.ShowDialog(this) == DialogResult.OK) ImportPaths(dialog.FileNames); }
    void ImportFolder() { using var dialog = new FolderBrowserDialog { Description = "取り込むフォルダを選択してください" }; if (dialog.ShowDialog(this) == DialogResult.OK) ImportPaths([dialog.SelectedPath]); }
    void ImportPaths(string[] paths) { foreach (var path in paths) Log("取り込み：" + AssetIO.Import(Project, path, currentFolder)); RefreshFolders(); }
    static void ValidName(string name) { if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..") throw new ArgumentException("有効な名前を入力してください。"); }
    void NewFolder() { var name = Ask("フォルダ作成", "フォルダ名", "新しいフォルダ"); if (name == null) return; ValidName(name); Directory.CreateDirectory(AssetIO.Unique(Project.Resolve(currentFolder + "/" + name), true)); RefreshFolders(); }
    void NewScript()
    {
        if (World != null) return; var name = Ask("C#スクリプト作成", "クラス名（日本語も使えます）", "新しい動作"); if (name == null) return;
        if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name)) throw new ArgumentException("C#のクラス名として使える名前を指定してください。");
        var path = Project.Resolve(currentFolder + "/" + name + ".cs"); if (File.Exists(path) || Compiler.Types.Keys.Any(k => k == name || k.EndsWith("." + name))) throw new InvalidOperationException("同名のスクリプトが存在します。");
        File.WriteAllText(path, ScriptCompiler.Template(name), new UTF8Encoding(false)); RefreshFolders(); OpenCode(Project.Relative(path)); CompileScripts(false); bottomTabs.SelectedTab = codeTab;
    }
    bool ConfirmCode()
    {
        if (codePath.Length == 0 || code.Text == savedCode) return true; var result = MessageBox.Show(this, "編集中のC#スクリプトを保存しますか？", "スクリプトの保存", MessageBoxButtons.YesNoCancel);
        if (result == DialogResult.Cancel) return false; if (result == DialogResult.Yes) SaveCode(); else { loading = true; code.Text = savedCode; loading = false; codeTab.Text = "C#スクリプト"; } return true;
    }
    internal void OpenCode(string relative) { if (!ConfirmCode()) return; codePath = relative; savedCode = File.ReadAllText(Project.Resolve(relative)); loading = true; code.Text = savedCode; loading = false; codeTab.Text = "C#スクリプト"; bottomTabs.SelectedTab = codeTab; status.Text = relative; }
    void SaveCode() { if (codePath.Length == 0) return; File.WriteAllText(Project.Resolve(codePath), code.Text, new UTF8Encoding(false)); savedCode = code.Text; codeTab.Text = "C#スクリプト"; Log("C#を保存：" + codePath); }
    void AttachCurrentScript()
    {
        if (Selected == null || World != null || codePath.Length == 0) { Log("追加先オブジェクトとC#スクリプトを選択してください。"); return; }
        SaveCode(); if (!CompileScripts(false)) return; var classes = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code.Text).GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().Select(c => c.Identifier.ValueText).ToHashSet();
        var matches = Compiler.Types.Where(t => classes.Contains(t.Value.Name)).ToArray(); if (matches.Length == 0) throw new InvalidOperationException("OhajikiBehaviourを継承した公開クラスがありません。");
        var chosen = matches.Length == 1 ? matches[0].Key : Ask("スクリプト追加", "追加するクラス名：\n" + string.Join("、", matches.Select(m => m.Key)), matches[0].Key);
        if (chosen == null) return; if (!Compiler.Types.ContainsKey(chosen)) throw new InvalidOperationException("クラスが見つかりません。"); history.Record(Scene); Selected.AddComponent("Script").Set("Class", chosen); SelectObject(Selected); Changed();
    }
    void SavePrefab()
    {
        if (World != null || Selected == null) return; var name = Ask("プレハブ保存", "プレハブ名", Selected.Name); if (name == null) return; ValidName(name);
        var ids = Scene.Descendants(Selected.Id); var prefab = new SceneData { Name = name, Width = Scene.Width, Height = Scene.Height, Objects = JsonIO.Clone(Scene.Objects.Where(o => ids.Contains(o.Id)).ToList()) }; var root = prefab.Find(Selected.Id)!; root.ParentId = null;
        Project.SaveScene(Project.Relative(AssetIO.Unique(Project.Resolve("Assets/Prefabs/" + name + ".prefab.json"))), prefab); RefreshFolders(); Log("プレハブを保存：" + name);
    }
    void ExportPlayer()
    {
        if (World != null || !ConfirmCode() || !ConfirmSave() || !CompileScripts(true)) return;
        foreach (var scenePath in Project.SceneFiles()) { var scene = Project.LoadScene(scenePath); foreach (var t in scene.Transitions) Project.LoadScene(t.Target); }
        Project.LoadScene(Project.Settings.StartScene);
        using var dialog = new FolderBrowserDialog { Description = "書き出し先の親フォルダ（新しい出力フォルダを作成します）", InitialDirectory = Project.Root }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var destination = AssetIO.Unique(Path.Combine(dialog.SelectedPath, "OhajikiPlayer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")), true);
        if (destination.StartsWith(Project.Assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Assetsの外を指定してください。");
        Directory.CreateDirectory(destination); CopyDirectory(AppContext.BaseDirectory, Path.Combine(destination, "bin")); CopyDirectory(Project.Assets, Path.Combine(destination, "Assets")); File.Copy(Project.Resolve("Ohajiki.project.json"), Path.Combine(destination, "Ohajiki.project.json"));
        File.WriteAllText(Path.Combine(destination, "Play.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"bin\\OhajikiEngine.exe\" --player\r\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(destination, "起動方法.txt"), "Play.cmdを実行してください。\r\nWindows 10/11 x64、.NET 10 Desktop Runtimeが必要です。\r\n描画はDirectX 12です。Unityは必要ありません。\r\n", new UTF8Encoding(false)); Log("書き出し完了：" + destination); MessageBox.Show(this, destination, "Windowsプレイヤーを書き出しました");
    }
    internal static void CopyDirectory(string source, string destination)
    {
        var fullSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar); var fullDestination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        if (fullDestination.Equals(fullSource, StringComparison.OrdinalIgnoreCase) || fullDestination.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("コピー元フォルダの中へは書き出せません。");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) if (!file.EndsWith(".bak") && !file.EndsWith(".autosave") && !file.EndsWith(".pdb")) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var folder in Directory.EnumerateDirectories(source)) if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }
    void ShowHelp()
    {
        var path = Project.Resolve("使い方.txt"); if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else MessageBox.Show(this, "GameEngineフォルダの「使い方.txt」を開いてください。", "使い方");
    }
    void EditorClosing(object? sender, FormClosingEventArgs e)
    {
        try { if (World?.Game is {} game&&!game.TrySaveProgress()){e.Cancel=true;MessageBox.Show(this,"進行を保存できません。保存先を確認して再試行してください。\n\n"+game.SaveError,"保存エラー");return;} if (!ConfirmCode() || !ConfirmSave()) { e.Cancel = true; return; } } catch (Exception ex) { e.Cancel = true; Log(ex.Message); return; }
        closing = true; timer.Stop(); World?.Dispose(); Renderer?.Dispose(); Compiler.Dispose(); timer.Dispose(); assets.LargeImageList?.Dispose();
    }
    internal static string? Ask(string title, string label, string value)
    {
        using var dialog = new Form { Text = title, Size = new Size(490, 205), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Yu Gothic UI", 10) };
        var caption = new Label { Text = label, Left = 18, Top = 15, Width = 440, Height = 40 }; var input = new TextBox { Text = value, Left = 18, Top = 60, Width = 438 }; var ok = new Button { Text = "決定", DialogResult = DialogResult.OK, Left = 250, Top = 106, Width = 100 }; var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Left = 360, Top = 106, Width = 100 };
        dialog.Controls.AddRange([caption, input, ok, cancel]); dialog.AcceptButton = ok; dialog.CancelButton = cancel; return dialog.ShowDialog() == DialogResult.OK ? input.Text.Trim() : null;
    }
    internal void MarkCleanForTest() => savedScene = JsonIO.Serialize(Scene);
}

static class TreeExtensions
{
    public static bool IsEditing(this TreeView tree) => tree.SelectedNode?.IsEditing ?? false;
}

sealed class SceneSettingsForm : Form
{
    public SceneData Result { get; private set; }
    public SceneSettingsForm(EngineProject project, SceneData source)
    {
        Result = JsonIO.Clone(source); Text = "シーン設定と画面遷移"; Size = new Size(820, 560); StartPosition = FormStartPosition.CenterParent; Font = new Font("Yu Gothic UI", 10); MinimumSize = new Size(740, 450);
        var fields = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 105, Padding = new Padding(12), WrapContents = true };
        var name = new TextBox { Text = source.Name, Width = 225 }; var width = new NumericUpDown { Minimum = 64, Maximum = 8192, Value = source.Width, Width = 90 }; var height = new NumericUpDown { Minimum = 64, Maximum = 8192, Value = source.Height, Width = 90 }; var background = new TextBox { Text = source.Background, Width = 100 };
        foreach (var (label, control) in new (string, Control)[] { ("シーン名", name), ("幅", width), ("高さ", height), ("背景色", background) }) { fields.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(5, 5, 4, 0) }); fields.Controls.Add(control); }
        fields.Controls.Add(new Label { Text = "遷移：ボタンのイベント名／C#のSendEventと一致させます。秒数は暗転と復帰の合計。", Width = 745, Height = 35, Margin = new Padding(5, 12, 0, 0) });
        var table = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = true, AllowUserToDeleteRows = true, BackgroundColor = Color.White, RowHeadersWidth = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, DataSource = new System.ComponentModel.BindingList<SceneTransition>(Result.Transitions) };
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "イベント名", DataPropertyName = "Event", FillWeight = 30 });
        var targets = project.SceneFiles().Concat(Result.Transitions.Select(t => t.Target)).Distinct().ToList();
        table.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "遷移先シーン", DataPropertyName = "Target", DataSource = targets, FillWeight = 55 });
        table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "秒数", DataPropertyName = "FadeSeconds", FillWeight = 15 }); table.DataError += (_, e) => { e.ThrowException = false; };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) }; var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 115 }; var ok = new Button { Text = "適用", Width = 100 }; buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        ok.Click += (_, _) => { try { table.EndEdit(); BindingContext?[table.DataSource!]?.EndCurrentEdit(); Result.Name = name.Text; Result.Width = (int)width.Value; Result.Height = (int)height.Value; ColorTranslator.FromHtml(background.Text); Result.Background = background.Text; Result.Validate(); foreach (var transition in Result.Transitions) project.LoadScene(transition.Target); DialogResult = DialogResult.OK; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "設定を確認してください"); } };
        Controls.Add(table); Controls.Add(fields); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
    }
}
