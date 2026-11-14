using Ohajiki.Portable;
using System.Text.Json;

namespace Ohajiki.Engine;

public static class GameData
{
    public static MasterGameData ImportWorkbook(EngineProject project,string source)
    {
        const string target="Assets/Data/MasterData.xlsx";
        Directory.CreateDirectory(project.Resolve("Library"));
        string temporary="Library/MasterData-"+Guid.NewGuid().ToString("N")+".xlsx";
        try
        {
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var output=File.Create(project.Resolve(temporary)))input.CopyTo(output);
            var data=Load(project,temporary);
            if(MasterDataImporter.Hash(source)!=data.sourceHash)throw new IOException("読み込み中に元のMasterDataが変更されました。保存してから再実行してください。");
            var destination=project.Resolve(target);if(File.Exists(destination))File.Copy(destination,destination+".bak",true);
            File.Move(project.Resolve(temporary),destination,true);
            data.sourceFile=Path.GetFileName(destination);return data;
        }
        finally{if(File.Exists(project.Resolve(temporary)))File.Delete(project.Resolve(temporary));}
    }
    public static void InstallScene(EngineProject project)
    {
        var scene=new SceneData{Name="おはじきチェインズ",Width=720,Height=1280};
        using(var preview=new GameApplication(project,scene,new InputState(),_=>{})) { }
        var controller=scene.CreateObject("おはじきチェインズ・ゲーム管理");controller.AddComponent("OhajikiGame");
        const string path="Assets/Scenes/OhajikiChains.scene.json";project.SaveScene(path,scene);
        project.Settings.StartScene=project.Settings.LastScene=path;project.SaveSettings();
    }
    public static MasterGameData Load(EngineProject project, string workbook = "Assets/Data/MasterData.xlsx")
    {
        T Read<T>(string file) => JsonUtility.FromJson<T>(File.ReadAllText(project.Resolve("Assets/Data/"+file+".json")));
        var home=Read<HubConfig>("HubConfig");var battle=Read<BattleTuning>("BattleTuning");home.shop=Read<ShopConfig>("ShopConfig");
        var textures=Read<Dictionary<string,Sprite>>("Textures");
        MasterDataImporter.ResolveTexture=id=>
        {
            if(!textures.TryGetValue(id,out var texture))
            {
                if(id.StartsWith("Assets/",StringComparison.Ordinal)&&AssetIO.IsTexture(id))texture=new Sprite{Path=id};
                else throw new InvalidDataException("テクスチャ参照がありません："+id);
            }
            if(!File.Exists(project.Resolve(texture.Path)))throw new FileNotFoundException("テクスチャがありません："+texture.Path);
            return texture;
        };
        using var stage=MasterDataImporter.Prepare(project.Resolve(workbook),home,battle);
        var result=stage.Data;
        // The current workbook leaves portrait IDs empty. Use the existing Unity
        // element artwork as the presentation placeholder, without editing master
        // rows or substituting any gameplay values. Explicit texture IDs win.
        foreach(var spirit in result.home.catalog.Concat(result.npcs.SelectMany(n=>n.deck)))
            if(spirit.portrait==null)spirit.portrait=new Sprite{Path="Assets/Imported/Art/"+new[]{"Fire","Water","Wood"}[(int)spirit.element]+".png"};
        foreach(var quest in result.home.quests.quests)
            if(quest.enemyPortrait==null)quest.enemyPortrait=quest.enemyDeck[0].portrait;
        // A new play session must never retain the previous project's constants.
        Resources.Master=result;MasterConstants.ResetCache();
        typeof(MasterGameData).GetMethod("ResetRuntimeLookup",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public)?.Invoke(null,null);
        return result;
    }
    public static string Describe(MasterGameData data)=>$"スピリター {data.home.catalog.Length} / NPC {data.npcs.Length} / クエスト {data.home.quests.quests.Length} / ガチャ {data.home.banners.Length} / 商品 {data.home.shop.products.Length}\nMasterData SHA256: {data.sourceHash}";
}
