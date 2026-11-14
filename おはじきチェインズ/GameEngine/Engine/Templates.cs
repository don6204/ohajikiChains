namespace Ohajiki.Engine;

public static class Templates
{
    public static void EnsureProject(EngineProject project)
    {
        foreach (var folder in new[] { "Scenes", "Textures", "Scripts", "Prefabs", "Audio" }) Directory.CreateDirectory(project.Resolve("Assets/" + folder));
        if (!File.Exists(project.Resolve("Ohajiki.project.json")))
        {
            foreach (var (path, scene) in new[] { ("Title", Title()), ("Home", Home()), ("Battle", Battle()), ("Effects", Effects()) })
                if (!File.Exists(project.Resolve($"Assets/Scenes/{path}.scene.json"))) project.SaveScene($"Assets/Scenes/{path}.scene.json", scene);
            project.SaveSettings();
        }
        var sample = project.Resolve("Assets/Scripts/ゆっくり回転.cs"); if (!File.Exists(sample)) File.WriteAllText(sample, ScriptCompiler.Template("ゆっくり回転"), new System.Text.UTF8Encoding(false));
    }
    public static GameObject Sprite(SceneData scene, string name, float x, float y, float width, float height, string color, string shape = "矩形", int order = 0)
    {
        var o = scene.CreateObject(name); o.Transform = new() { X = x, Y = y, Width = width, Height = height }; o.Order = order;
        var s = o.AddComponent("Sprite"); s.Set("Color", color); s.Set("Shape", shape); return o;
    }
    public static GameObject Text(SceneData scene, string name, string content, float x, float y, float width, float height, float size, string color = "#F7F0DD", int order = 20)
    {
        var o = scene.CreateObject(name); o.Transform = new() { X = x, Y = y, Width = width, Height = height }; o.Order = order; var c = o.AddComponent("Text"); c.Set("Text", content); c.Set("FontSize", size); c.Set("Color", color); return o;
    }
    static GameObject Button(SceneData scene, string label, float y, string eventName)
    {
        var o = Sprite(scene, label, 360, y, 500, 90, "#2A716D", order: 15); o.AddComponent("Button").Set("Event", eventName);
        var text = o.AddComponent("Text"); text.Set("Text", label); text.Set("FontSize", 32); return o;
    }
    public static SceneData Title()
    {
        var s = new SceneData { Name = "タイトル", Background = "#10282F" };
        Text(s, "タイトル", "おはじき\nチェインズ", 360, 420, 660, 240, 78);
        Text(s, "説明", "シーンと演出を組み立てる", 360, 610, 600, 65, 26, "#9EBAB7");
        var orb = Sprite(s, "タイトルのおはじき", 360, 790, 135, 135, "#F0C16F", "リング"); orb.AddComponent("ParticleEmitter").Set("Rate", 8);
        var script = orb.AddComponent("Script"); script.Set("Class", "ゆっくり回転"); script.Set("field:Speed", "25");
        Button(s, "タップしてはじめる", 1030, "ホームへ"); s.Transitions.Add(new() { Event = "ホームへ", Target = "Assets/Scenes/Home.scene.json", FadeSeconds = .5f });
        Text(s, "バージョン", "エンジン用サンプル  0.1", 360, 1210, 600, 45, 20, "#9EBAB7"); return s;
    }
    public static SceneData Home()
    {
        var s = new SceneData { Name = "ホーム", Background = "#152D34" };
        Text(s, "見出し", "制作プレビュー", 360, 200, 620, 120, 60);
        Text(s, "ガイド", "シーン遷移とコンポーネントの動作例", 360, 320, 640, 90, 26, "#A7C7BF");
        Button(s, "盤面で試射する", 520, "バトルへ"); Button(s, "エフェクトを確認する", 680, "演出へ"); Button(s, "タイトルへ戻る", 960, "タイトルへ");
        s.Transitions.AddRange([new() { Event = "バトルへ", Target = "Assets/Scenes/Battle.scene.json" }, new() { Event = "演出へ", Target = "Assets/Scenes/Effects.scene.json" }, new() { Event = "タイトルへ", Target = "Assets/Scenes/Title.scene.json" }]); return s;
    }
    public static SceneData Battle()
    {
        var s = new SceneData { Name = "バトル盤面・試射", Background = "#132C37" };
        Text(s, "見出し", "盤面・ショットの調整", 360, 58, 690, 72, 34);
        var board = Sprite(s, "盤面", 360, 650, 624, 940, "#203F4B", order: 0); board.AddComponent("Board"); BoardTools.Generate(s, board);
        Text(s, "操作ガイド", "再生後、下のボール付近から上へスワイプ", 360, 1220, 700, 55, 22, "#A6C8C7");
        var ball = Sprite(s, "スピリットボール", 360, 1080, 52, 52, "#FFE2A0", "円", 30); ball.AddComponent("BoxCollider"); ball.AddComponent("RigidBody"); ball.AddComponent("Shot"); ball.AddComponent("Spirit");
        var hits = Text(s, "ヒット数", "0 HIT", 220, 145, 300, 85, 45, "#F9CD71"); hits.Transform.Rotation = -10; hits.AddComponent("BattleReadout");
        var skill = Text(s, "効果度", "効果度  Lv.1", 520, 145, 310, 85, 30); skill.AddComponent("BattleReadout").Set("Value", "効果度");
        var attack = Text(s, "攻撃値", "攻撃値  0", 360, 1165, 400, 60, 27, "#F0C989"); attack.AddComponent("BattleReadout").Set("Value", "攻撃値");
        // Layout values are editable preview defaults, not new game balance requirements.
        return s;
    }
    public static SceneData Effects()
    {
        var s = new SceneData { Name = "エフェクト調整", Background = "#15252D" };
        Text(s, "見出し", "エフェクトの試作", 360, 180, 620, 110, 52);
        Text(s, "説明", "発生数・寿命・色・速度を\nインスペクターで調整できます", 360, 320, 650, 140, 27, "#A9C7C3");
        var emitter = Sprite(s, "エネルギーの発生源", 360, 700, 130, 130, "#EAC46E", "リング"); emitter.AddComponent("ParticleEmitter");
        Button(s, "ホームへ戻る", 1060, "戻る"); s.Transitions.Add(new() { Event = "戻る", Target = "Assets/Scenes/Home.scene.json" }); return s;
    }
}
