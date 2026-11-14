using System.IO;
using System.Linq;
using Ohajiki;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class PrototypeBuilder
{
    const string Root = "Assets/Ohajiki/";
    // Run without -quit: resource import completes asynchronously on an Editor update.
    public static void ImportEssentials()
    {
        if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) { EditorApplication.Exit(0); return; }
        AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += () => EditorApplication.Exit(0);
        AssetDatabase.importPackageFailed += (_, error) => { Debug.LogError(error); EditorApplication.Exit(1); };
        TMP_PackageResourceImporter.ImportResources(true, false, false);
    }
    [MenuItem("Ohajiki/Create or refresh prototype scene")]
    public static void Setup()
    {
        if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
            throw new System.InvalidOperationException("Run PrototypeBuilder.ImportEssentials without -quit first, then build.");
        AssetDatabase.Refresh();
        var hitTexture=(TextureImporter)AssetImporter.GetAtPath(Root+"Resources/UI/HitCounterBurst.png");
        if(hitTexture!=null && hitTexture.textureType!=TextureImporterType.Sprite)
        {
            hitTexture.textureType=TextureImporterType.Sprite;hitTexture.spriteImportMode=SpriteImportMode.Single;
            hitTexture.alphaIsTransparency=true;hitTexture.mipmapEnabled=false;
            hitTexture.maxTextureSize=2048;hitTexture.SaveAndReimport();
        }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Fonts/Japanese.asset");
        if (font == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/RocknRollOne-Regular.ttf");
            font = TMP_FontAsset.CreateFontAsset(source, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "Japanese";
            AssetDatabase.CreateAsset(font, Root + "Fonts/Japanese.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
        }
        string[] files = { "Fire", "Water", "Wood" };
        var portraits = new Sprite[3];
        for (int i = 0; i < files.Length; i++)
        {
            string path = Root + "Art/" + files[i] + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.maxTextureSize = 512; importer.SaveAndReimport();
            portraits[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        var tuning = AssetDatabase.LoadAssetAtPath<BattleTuning>(Root + "BattleTuning.asset");
        if (tuning == null) { tuning = ScriptableObject.CreateInstance<BattleTuning>(); AssetDatabase.CreateAsset(tuning, Root + "BattleTuning.asset"); }
        ConfigureDeck(tuning, portraits);
        ConfigureVisuals(tuning,font);
        if (!tuning.Validate(out string error)) throw new System.InvalidOperationException(error);
        // Persist newly added presentation fields while retaining all authored values.
        EditorUtility.SetDirty(tuning);
        const string scenePath = Root + "Scenes/Prototype.unity";
        var scene = File.Exists(scenePath) ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single) :
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = Object.FindFirstObjectByType<Camera>();
        if (camera == null)
        {
            camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f, .035f, .055f);
        }
        var battle = Object.FindFirstObjectByType<PrototypeBattle>();
        if (battle == null) battle = new GameObject("Ohajiki Battle").AddComponent<PrototypeBattle>();
        battle.Configure(tuning, font);
        battle.enabled=false;
        EditorUtility.SetDirty(battle);
        var home=ConfigureHome(tuning);
        var app=battle.GetComponent<PrototypeApp>();
        if(app==null)app=battle.gameObject.AddComponent<PrototypeApp>();
        app.Configure(tuning,home,font);app.enabled=true;EditorUtility.SetDirty(app);
        Directory.CreateDirectory(Root + "Scenes");
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        PlayerSettings.productName = "Ohajiki Chains Prototype";
        PlayerSettings.bundleVersion = "1.020";
        PlayerSettings.companyName = "OhajikiPrototype";
        PlayerSettings.defaultScreenWidth = 540; PlayerSettings.defaultScreenHeight = 960;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        AssetDatabase.SaveAssets();
        Debug.Log("SETUP HOME PASS: preserved battle assets, home, decks, upgrades, free trial gacha.");
    }
    static HubConfig ConfigureHome(BattleTuning tuning)
    {
        string path=Root+"HubConfig.asset";
        var home=AssetDatabase.LoadAssetAtPath<HubConfig>(path);
        if(home==null)
        {
            home=ScriptableObject.CreateInstance<HubConfig>();
            home.catalog=new SpiritDefinition[18];
            for(int i=0;i<12;i++)home.catalog[i]=tuning.playerDeck[i];
            string[] names={"アカリビ","ミズハ","モリノネ","エンガ","ミナモ","ワカバ"};
            for(int i=12;i<18;i++)
            {
                string spiritPath=Root+"Data/Spirits/Spirit_"+i.ToString("00")+".asset";
                var spirit=AssetDatabase.LoadAssetAtPath<SpiritDefinition>(spiritPath);
                if(spirit==null)
                {
                    spirit=Object.Instantiate(tuning.playerDeck[i%3]);
                    spirit.displayName=names[i-12];spirit.rarity=i<15?2:3;
                    spirit.skill=i<15?Skill.PercentHeal:Skill.PercentGuard;
                    spirit.skillName=i<15?"めぐみの光":"精霊の加護";
                    spirit.skillPower=i<15?new[]{.04f,.06f,.08f,.1f,.13f}:new[]{.1f,.15f,.2f,.25f,.3f};
                    AssetDatabase.CreateAsset(spirit,spiritPath);
                }
                home.catalog[i]=spirit;
            }
            string[] titles={"火のスピリターの集い","水のスピリターの集い","木のスピリターの集い","みんなのスピリター召喚"};
            string[] descriptions={"燃える想いを、仲間の力に。","澄んだ水音と、つながる絆。","芽吹く命と、新たな出会い。","火・水・木の仲間が勢ぞろい。"};
            home.banners=Enumerable.Range(0,4).Select(b=>new GachaBanner{
                title=titles[b],description=descriptions[b],featuredIndex=12+b%3,
                pool=Enumerable.Range(0,18).Where(i=>b==3||(int)home.catalog[i].element==b)
                    .Select(i=>new GachaEntry{spiritIndex=i,weight=1}).ToArray()
            }).ToArray();
            AssetDatabase.CreateAsset(home,path);
        }
        ShopPrototypeData.Configure(home,tuning);
        QuestPrototypeData.Configure(home);
        if(home.gachaPresentationVersion<20261109)
        {
            home.firstSingleGems=1;home.singleGems=5;home.tenGems=50;home.tenBonusGems=1;home.featuredCycleSeconds=5;
            foreach(var banner in home.banners)
            {
                if(banner.featuredIndices==null || banner.featuredIndices.Length==0)
                    banner.featuredIndices=new[]{banner.featuredIndex}.Concat(banner.pool.Where(e=>e.weight>0 && home.catalog[e.spiritIndex].rarity==5).Select(e=>e.spiritIndex)).Distinct().Take(3).ToArray();
                if(banner.description=="有償枠の排出率を無料で動作確認。")banner.description="★3以上のスピリターが登場。共通の宝石で試作中。";
            }
            home.gachaPresentationVersion=20261109;EditorUtility.SetDirty(home);
        }
        // Only the dated migration changes old specification data. Rebuilds retain edits.
        if(!home.Validate(out string error))throw new System.InvalidOperationException(error);
        return home;
    }
    static void ConfigureVisuals(BattleTuning tuning,TMP_FontAsset font)
    {
        // Keep the mobile SDF outline shader variant in player builds as well as the Editor.
        string outlinePath=Root+"Resources/UI/FeedbackOutline.mat";
        if(AssetDatabase.LoadAssetAtPath<Material>(outlinePath)==null)
        {
            var material=new Material(font.material){name="Feedback Outline"};
            material.EnableKeyword("OUTLINE_ON");material.SetFloat("_OutlineWidth",.18f);
            material.SetColor("_OutlineColor",new Color(.26f,.12f,.025f,1));
            AssetDatabase.CreateAsset(material,outlinePath);
        }
        foreach(string name in new[]{"SunnyRuins","SandstoneBoard"})
        {
            string path=Root+"Resources/UI/"+name+".png";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new System.InvalidOperationException("Missing UI illustration: "+path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.maxTextureSize=name=="SunnyRuins"?2048:1024;importer.mipmapEnabled=false;
            importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        if(tuning.presentationVersion<20261108)
        {
            tuning.contactEffectIntensity=1.15f;tuning.contactEffectSeconds=.64f;
            tuning.damageNumberPunch=.32f;tuning.damageCounterSpeed=19;tuning.trailOpacity=.65f;
            tuning.damageBankShake=9;tuning.damageBankTilt=4;tuning.hitShake=11;
            tuning.chargeSeconds=.85f;tuning.normalImpactSeconds=.72f;tuning.cutInSeconds=.8f;tuning.impactSeconds=.75f;
            tuning.presentationVersion=20261108;EditorUtility.SetDirty(tuning);
        }
        if(tuning.presentationVersion<20261109)
        {
            tuning.damageNumberPunch=.5f;tuning.damageBankShake=17;tuning.damageBankTilt=8;tuning.damageCounterSpeed=24;
            tuning.hitShake=19;tuning.damageFlightSeconds=.48f;tuning.normalImpactSeconds=.9f;tuning.impactSeconds=.85f;
            tuning.presentationVersion=20261109;EditorUtility.SetDirty(tuning);
        }
        if(tuning.presentationVersion<20261111)
        {
            tuning.hitShake=tuning.damageBankShake=tuning.damageBankTilt=tuning.damageNumberPunch=0;
            tuning.presentationVersion=20261111;EditorUtility.SetDirty(tuning);
        }
    }
    static void ConfigureDeck(BattleTuning tuning, Sprite[] portraits)
    {
        // One-time migration only. Never overwrite user-authored character/deck data on rebuild.
        bool missing = CanCreateDefaultDeck(tuning.playerDeck, Root + "Data/Spirits");
        if (missing)
        {
            Directory.CreateDirectory(Root + "Data/Spirits");
            tuning.playerDeck = new SpiritDefinition[12];
            string[] names = { "ヒバナマル", "シズネ", "コノモリ" };
            for (int i = 0; i < 12; i++)
            {
                string path = Root + "Data/Spirits/Spirit_" + i.ToString("00") + ".asset";
                var spirit = AssetDatabase.LoadAssetAtPath<SpiritDefinition>(path);
                if (spirit == null)
                {
                    spirit = ScriptableObject.CreateInstance<SpiritDefinition>();
                    int variant = i / 3;
                    spirit.displayName = names[i % 3] + (variant + 1);
                    spirit.element = (Element)(i % 3); spirit.portrait = portraits[i % 3];
                    spirit.baseHp = 120; spirit.baseAttack = 220;
                    spirit.skill = variant == 0 ? Skill.Attack : variant == 1 ? Skill.Heal : variant == 2 ? Skill.Guard : Skill.PercentAttack;
                    spirit.skillName = variant == 0 ? "スピリットバースト" : variant == 1 ? "いやしの光" : variant == 2 ? "守りの結界" : "エレメントブレイク";
                    spirit.skillPower = variant == 0 ? new float[] {100,145,190,235,280} :
                        variant == 1 ? new float[] {65,100,140,180,230} :
                        variant == 2 ? new float[] {35,50,70,90,115} : new float[] {.04f,.06f,.08f,.10f,.13f};
                    if (i == 4 || i == 5 || i == 7 || i == 8 || i == 10)
                    {
                        spirit.skill = i == 4 ? Skill.AttackUp : i == 5 ? Skill.SpeedUp : i == 7 ? Skill.Cleanse :
                            i == 8 ? Skill.AttackDown : Skill.SpeedDown;
                        spirit.skillName = i == 4 ? "勇気の炎" : i == 5 ? "風の加速" : i == 7 ? "清めの光" :
                            i == 8 ? "衰弱の霧" : "束縛の蔦";
                        spirit.statusTurns = 2;
                        spirit.skillPower = i == 4 ? new[] {1.2f,1.3f,1.4f,1.5f,1.6f} :
                            i == 5 ? new[] {1.2f,1.4f,1.6f,1.8f,2f} :
                            i == 7 ? new[] {0f,0f,0f,0f,0f} : new[] {.9f,.8f,.7f,.6f,.5f};
                    }
                    if (i == 0 || i == 2)
                    {
                        spirit.actionSkill = ActionSkill.Trap;
                        spirit.trapTargetElement = i == 0 ? Element.Water : Element.Fire;
                        spirit.trapDamage = 100;
                        spirit.trapOpponentTurns = 2;
                    }
                    if (i == 1)
                    {
                        spirit.actionSkill = ActionSkill.Split;
                        spirit.splitContactThreshold = 3;
                        spirit.splitTotalProjectiles = 3;
                    }
                    AssetDatabase.CreateAsset(spirit,path);
                }
                tuning.playerDeck[i] = spirit;
            }
            EditorUtility.SetDirty(tuning);
        }
        if(CanCreateDefaultDeck(tuning.enemyDeck, Root + "Data/Enemies"))
        {
            if (tuning.playerDeck == null || tuning.playerDeck.Length != SpiritDeck.Size || tuning.playerDeck.Any(s => s == null))
                throw new System.InvalidOperationException("味方デッキの未設定枠を修正してから敵データを生成してください。");
            // Independent assets so editing enemy HP/power never changes player characters.
            Directory.CreateDirectory(Root + "Data/Enemies");
            tuning.enemyDeck = new SpiritDefinition[12];
            for(int i=0;i<12;i++)
            {
                string path=Root+"Data/Enemies/Enemy_"+i.ToString("00")+".asset";
                var spirit=AssetDatabase.LoadAssetAtPath<SpiritDefinition>(path);
                if(spirit == null)
                {
                    spirit=Object.Instantiate(tuning.playerDeck[i]);
                    spirit.displayName="敵 "+spirit.displayName;
                    AssetDatabase.CreateAsset(spirit,path);
                }
                tuning.enemyDeck[i]=spirit;
            }
            EditorUtility.SetDirty(tuning);
        }
    }
    static bool CanCreateDefaultDeck(SpiritDefinition[] deck, string directory)
    {
        // Only bootstrap a project that has never had deck data. An incomplete authored
        // deck must fail validation without silently replacing any of the user's slots.
        return !Directory.Exists(directory) && (deck == null || deck.Length == 0 || deck.All(s => s == null));
    }
    public static void Build()
    {
        Setup();
        // D3D11 avoids the D3D12 scratch allocator crash reproduced on the target Windows PC.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        string dataInputStatus=DataInputImporter.PrepareBuildData();
        var imported=AssetDatabase.LoadAssetAtPath<MasterGameData>(MasterDataImporter.OutputPath);
        var buildTuning=imported!=null?imported.battle:AssetDatabase.LoadAssetAtPath<BattleTuning>(Root+"BattleTuning.asset");
        string chargePercentages=string.Join(", ",buildTuning.specialChargeProbabilities.Select(p=>
            (p*100).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"%"));
        string normalPercentage=((1-buildTuning.specialChargeProbabilities.Sum())*100).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
        const string output="Build/20261114_HpBarShakeDuration";
        Directory.CreateDirectory(output);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { Root + "Scenes/Prototype.unity" },
            locationPathName = output+"/OhajikiChains.exe",
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("Build failed: " + report.summary.result);
        File.Copy("遊び方.txt",output+"/遊び方.txt",true);
        File.Copy("Docs/20261113_MasterData取込.txt",output+"/仕様対応とデータ入力.txt",true);
        Directory.CreateDirectory(output+"/Docs");
        File.Copy("Docs/20261111_01_仕様対応とデータ入力.txt",output+"/Docs/20261111_01_仕様対応とデータ入力.txt",true);
        if(File.Exists("Docs/20261111_01_検証結果.txt"))
            File.Copy("Docs/20261111_01_検証結果.txt",output+"/Docs/20261111_01_検証結果.txt",true);
        if(File.Exists("Docs/20261111_チャージボール変更と検証.txt"))
            File.Copy("Docs/20261111_チャージボール変更と検証.txt",output+"/Docs/20261111_チャージボール変更と検証.txt",true);
        File.Copy("Docs/20261111_03_変更と検証.txt",output+"/Docs/20261111_03_変更と検証.txt",true);
        File.Copy("Docs/20261111_03_ショット領域調整.txt",output+"/Docs/20261111_03_ショット領域調整.txt",true);
        File.Copy("Docs/20261113_MasterData取込.txt",output+"/Docs/20261113_MasterData取込.txt",true);
        File.WriteAllText(output+"/build-info.txt","Ohajiki Chains "+PlayerSettings.bundleVersion+" / "+output+"\nUnity "+Application.unityVersion+
            "\nWindows x64 / Mono / D3D11\nEvent 3 / Showdown 3 x 3 / Challenge 5\nData input: "+dataInputStatus+
            "\nCharge probabilities: "+chargePercentages+" (normal "+normalPercentage+"%); warp to opponent launch position."+
            "\n8x8 board; no screen shake; gold all-clear scroll 1.5 seconds; refill after 5 moving seconds."+
            "\nCharge diameter 44; vertical centers 22%-78%; rims clear both launch zones."+
            "\nDamage UI: cumulative normal damage, tilted digits pop/shake per reference video; fixed burst center; yellow to red at "+buildTuning.damageColorFullAt+" damage."+
            "\nDamage UI size: +10% per 500 damage, capped at 1.5x. Latest planner reply supersedes the earlier 100-damage proposal."+
            "\nNormal HP damage applies instantly once when the damage UI arrives. Target HP bar only: 5 UI units, 0.30-second decaying shake. No gradual drain. Shot -> zone -> normal damage flight -> impact -> slide -> skill damage readout."+
            "\nContact n: attack * (1 + (n-1) * "+buildTuning.contactAttackFactor.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+") * affinity * current projectile boost. First hit 100%; linear growth; resets each shot."+
            "\nHP zero: finish the active shot, zone, flight, impact, slide and skill before showing results. First defeat is preserved. Flight completion settles normal HP damage once; later frames never reapply it."+
            "\nLocal persistence: player-save.bin, typed binary schema v1 (OHAJKSAV / little endian), SHA256, atomic replacement and binary backup. Automatic migration of v1.015 JSON saves without editing the original. No mid-battle resume."+
            "\nBuilt UTC: "+System.DateTime.UtcNow.ToString("O"));
        File.Copy("Docs/20261114_HPバーシェイク時間調整.txt",output+"/変更内容と検証.txt",true);
        File.Copy("Docs/20261114_データ保存.txt",output+"/Docs/20261114_データ保存.txt",true);
        File.Copy("Docs/20261114_攻撃演出復元.txt",output+"/Docs/20261114_攻撃演出復元.txt",true);
        File.Copy("Docs/20261114_MasterData更新.txt",output+"/Docs/20261114_MasterData更新.txt",true);
        File.Copy("Docs/20261114_仕様更新対応.txt",output+"/Docs/20261114_仕様更新対応.txt",true);
        Debug.Log("BUILD PASS: " + report.summary.totalSize + " bytes");
    }
}
