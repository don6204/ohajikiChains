using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Ohajiki;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// A read-only XLSX boundary: validate all rows on copies before changing any asset.
public static class DataInputImporter
{
    const string Root="Assets/Ohajiki/";
    static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public sealed class InputRow
    {
        public string target,id,field,value,source;
    }
    [MenuItem("Ohajiki/Import dataInput_list.xlsx")]
    public static void ImportDefault()
    {
        string path=DefaultPath;
        if(!File.Exists(path))throw new FileNotFoundException("入力ファイルが見つかりません",path);
        Import(path);
    }
    static string DefaultPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../dataInput_list.xlsx"));
    public static bool IsStructuredMaster(string path)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
        {
            var workbook=zip.GetEntry("xl/workbook.xml");
            if(workbook==null)return false;
            var names=new HashSet<string>(ReadXml(workbook).Descendants(S+"sheet").Select(e=>(string)e.Attribute("name")));
            return names.Contains("00_入力ガイド")&&names.Contains("05_クエスト")&&names.Contains("17_取込定義");
        }
    }
    // The separately authored master has approval columns and relational tables.
    // It must never be interpreted as the legacy four-column override format.
    public static string PrepareBuildData(string path=null)
    {
        path=path??DefaultPath;
        if(!File.Exists(path))throw new FileNotFoundException("入力ファイルが見つかりません",path);
        if(IsStructuredMaster(path))
        {
            const string status="Structured XLSX master detected; import not connected. Existing Inspector data used; workbook preserved.";
            Debug.LogWarning("DATA INPUT: "+status);
            return status;
        }
        int count=Import(path);
        return count==0?"Empty XLSX; existing Inspector data used.":"Legacy XLSX overrides applied: "+count;
    }
    public static List<InputRow> ReadRows(string path)
    {
        if(IsStructuredMaster(path))throw new InvalidDataException("18シートの入力マスターは専用の取込処理が未接続です。原本の形式を変更しないでください。現在はInspectorの設定でビルドします。");
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
        {
            var sharedEntry=zip.GetEntry("xl/sharedStrings.xml");
            var shared=sharedEntry==null?Array.Empty<string>():ReadXml(sharedEntry).Descendants(S+"si")
                .Select(e=>string.Concat(e.Descendants(S+"t").Select(t=>t.Value))).ToArray();
            var result=new List<InputRow>();
            foreach(var sheet in zip.Entries.Where(e=>e.FullName.StartsWith("xl/worksheets/sheet",StringComparison.Ordinal)&&e.FullName.EndsWith(".xml",StringComparison.Ordinal)))
            {
                var rows=ReadXml(sheet).Descendants(S+"row").ToArray();bool header=false;
                foreach(var row in rows)
                {
                    var cells=new Dictionary<string,string>();
                    foreach(var c in row.Elements(S+"c"))
                    {
                        string address=(string)c.Attribute("r");
                        if(string.IsNullOrEmpty(address))throw new InvalidDataException(sheet.FullName+": セル番地がありません");
                        string column=new string(address.TakeWhile(char.IsLetter).ToArray());
                        string value=c.Element(S+"v")?.Value??"";
                        if(c.Element(S+"f")!=null)throw new InvalidDataException(address+": 数式ではなく確定した値を入力してください");
                        if((string)c.Attribute("t")=="s")value=shared[int.Parse(value,CultureInfo.InvariantCulture)];
                        if((string)c.Attribute("t")=="inlineStr")value=string.Concat(c.Descendants(S+"t").Select(t=>t.Value));
                        if(!string.IsNullOrWhiteSpace(value))cells[column]=value.Trim();
                    }
                    if(cells.Count==0)continue;
                    string Cell(string col)=>cells.TryGetValue(col,out string v)?v:"";
                    if(!header)
                    {
                        if((Cell("A")!="対象" && Cell("A")!="target") || !Cell("B").Equals("id",StringComparison.OrdinalIgnoreCase) ||
                            (Cell("C")!="項目"&&Cell("C")!="field") || (Cell("D")!="値"&&Cell("D")!="value"))
                            throw new InvalidDataException(sheet.FullName+": 先頭行を 対象 / ID / 項目 / 値 にしてください");
                        header=true;continue;
                    }
                    if(Cell("D")=="")continue; // Blank values preserve the Inspector value; numeric zero is an override.
                    if(cells.Keys.Any(k=>k!="A"&&k!="B"&&k!="C"&&k!="D"&&k!="E"))
                        throw new InvalidDataException(sheet.FullName+": データはA〜D列、補足はE列に入力してください");
                    if(Cell("A")==""||Cell("C")=="")throw new InvalidDataException("対象・項目が空欄です");
                    result.Add(new InputRow{target=Cell("A"),id=Cell("B"),field=Cell("C"),value=Cell("D"),source=sheet.FullName+":"+(string)row.Attribute("r")});
                    if(result.Count>10000)throw new InvalidDataException("入力行数の上限10000を超えています");
                }
            }
            return result;
        }
    }
    static XDocument ReadXml(ZipArchiveEntry entry)
    {
        if(entry.Length>10000000)throw new InvalidDataException("入力シートが大きすぎます");
        using(var stream=entry.Open())return XDocument.Load(stream);
    }
    public static int Import(string path,HubConfig config=null,BattleTuning settings=null)
    {
        var rows=ReadRows(path);
        if(rows.Count==0){Debug.Log("DATA INPUT: empty workbook; retained all provisional / Inspector values.");return 0;}
        var home=config??AssetDatabase.LoadAssetAtPath<HubConfig>(Root+"HubConfig.asset");
        var tuning=settings??AssetDatabase.LoadAssetAtPath<BattleTuning>(Root+"BattleTuning.asset");
        if(home==null||tuning==null||home.quests==null)throw new InvalidOperationException("先にプロトタイプをセットアップしてください");
        var copies=new Dictionary<ScriptableObject,ScriptableObject>();
        ScriptableObject Copy(ScriptableObject source)
        {
            if(source==null)throw new InvalidDataException("対象アセットがありません");
            if(!copies.TryGetValue(source,out var copy)){copy=Object.Instantiate(source);copy.name=source.name;copies.Add(source,copy);}
            return copy;
        }
        var seen=new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach(var row in rows)
            {
                object target;string kind=row.target.ToLowerInvariant();
                if((kind=="battle"||kind=="hub"||kind=="shop")&&!string.IsNullOrEmpty(row.id))
                    throw new InvalidDataException(row.source+": この対象のID列は空欄にしてください");
                switch(kind)
                {
                    case "battle":target=Copy(tuning);break;
                    case "hub":target=Copy(home);break;
                    case "shop":target=Copy(home.shop);break;
                    case "quest":target=((QuestCatalog)Copy(home.quests)).Find(row.id);break;
                    case "spirit":target=Copy(AssetDatabase.LoadAssetAtPath<SpiritDefinition>(Root+"Data/Spirits/"+SafeId(row.id)+".asset"));break;
                    case "enemy":target=Copy(AssetDatabase.LoadAssetAtPath<SpiritDefinition>(Root+"Data/Enemies/"+SafeId(row.id)+".asset"));break;
                    default:throw new InvalidDataException(row.source+": 未対応の対象 "+row.target);
                }
                string key=row.target.ToLowerInvariant()+":"+row.id+":"+row.field;
                if(!seen.Add(key))throw new InvalidDataException(row.source+": 同じ項目の重複入力 "+key);
                if(target==null)throw new InvalidDataException(row.source+": 不明なID "+row.id);
                ApplyValue(target,row.field,row.value);
            }
            var homeCopy=(HubConfig)Copy(home);var tuningCopy=(BattleTuning)Copy(tuning);
            var originalCatalog=homeCopy.catalog;var originalPlayer=tuningCopy.playerDeck;var originalEnemy=tuningCopy.enemyDeck;
            ScriptableObject Mapped(ScriptableObject value)=>value!=null&&copies.TryGetValue(value,out var copy)?copy:value;
            homeCopy.catalog=homeCopy.catalog.Select(s=>(SpiritDefinition)Mapped(s)).ToArray();
            homeCopy.quests=(QuestCatalog)Mapped(home.quests);homeCopy.shop=(ShopConfig)Mapped(home.shop);
            tuningCopy.playerDeck=tuningCopy.playerDeck.Select(s=>(SpiritDefinition)Mapped(s)).ToArray();
            tuningCopy.enemyDeck=tuningCopy.enemyDeck.Select(s=>(SpiritDefinition)Mapped(s)).ToArray();
            foreach(var copy in copies.Values)
            {
                if(copy is SpiritDefinition spirit && !spirit.IsValid)throw new InvalidDataException(spirit.name+": キャラ設定が不正です");
                ValidateNumbers(copy);
                if(copy is QuestCatalog catalog)foreach(var quest in catalog.quests)ValidateNumbers(quest);
            }
            if(!homeCopy.Validate(out string error)||!tuningCopy.Validate(out error))throw new InvalidDataException(error);
            homeCopy.catalog=originalCatalog;homeCopy.quests=home.quests;homeCopy.shop=home.shop;
            tuningCopy.playerDeck=originalPlayer;tuningCopy.enemyDeck=originalEnemy;
            Undo.RecordObjects(copies.Keys.ToArray(),"Import dataInput_list.xlsx");
            foreach(var pair in copies){EditorUtility.CopySerialized(pair.Value,pair.Key);EditorUtility.SetDirty(pair.Key);}
            AssetDatabase.SaveAssets();Debug.Log("DATA INPUT PASS: "+rows.Count+" overrides from "+path);return rows.Count;
        }
        finally{foreach(var copy in copies.Values)Object.DestroyImmediate(copy);}
    }
    static string SafeId(string id)
    {
        if(string.IsNullOrEmpty(id)||id.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'))throw new InvalidDataException("不正なアセットID: "+id);
        return id;
    }
    public static void ApplyValue(object target,string name,string text)
    {
        var field=target.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance);
        if(field==null||field.IsDefined(typeof(HideInInspector),true)||name=="id"||name=="groupId")
            throw new InvalidDataException("未対応の項目: "+name);
        field.SetValue(target,Parse(field.FieldType,text));
    }
    static object Parse(Type type,string text)
    {
        if(type==typeof(string))return text=="<empty>"?"":text;
        if(type==typeof(int))return int.Parse(text,NumberStyles.Integer,CultureInfo.InvariantCulture);
        if(type==typeof(float))
        {float value=float.Parse(text,CultureInfo.InvariantCulture);if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("有限の数値が必要です");return value;}
        if(type==typeof(bool))return text=="1"?true:text=="0"?false:bool.Parse(text);
        if(type.IsEnum)
        {var value=Enum.Parse(type,text,true);if(!Enum.IsDefined(type,value))throw new InvalidDataException("列挙値が範囲外です");return value;}
        if(type==typeof(int[])||type==typeof(float[]))
        {var values=text.Split(';');var array=Array.CreateInstance(type.GetElementType(),values.Length);for(int i=0;i<values.Length;i++)array.SetValue(Parse(type.GetElementType(),values[i]),i);return array;}
        throw new InvalidDataException("この項目はInspectorで設定してください: "+type.Name);
    }
    static void ValidateNumbers(object target)
    {
        foreach(var field in target.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
        {
            if(field.FieldType!=typeof(int)&&field.FieldType!=typeof(float))continue;
            double value=Convert.ToDouble(field.GetValue(target),CultureInfo.InvariantCulture);
            var min=field.GetCustomAttribute<MinAttribute>();var range=field.GetCustomAttribute<RangeAttribute>();
            if(double.IsNaN(value)||double.IsInfinity(value)||(min!=null&&value<min.min)||(range!=null&&(value<range.min||value>range.max)))
                throw new InvalidDataException(target.GetType().Name+"."+field.Name+": Inspectorで指定した範囲外です");
        }
    }
}
