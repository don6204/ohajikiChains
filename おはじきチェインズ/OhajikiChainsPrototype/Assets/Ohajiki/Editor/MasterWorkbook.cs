using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

// Reads only. A-column control markers are evaluated before any other cell on that row.
public sealed class MasterWorkbook
{
    static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public sealed class Row
    {
        public string Sheet;
        public int Number;
        public readonly Dictionary<string,string> Cells=new Dictionary<string,string>(StringComparer.Ordinal);
        public readonly Dictionary<string,string> Columns=new Dictionary<string,string>(StringComparer.Ordinal);
        public string Cell(string column)=>Cells.TryGetValue(column,out var value)?value:null;
        public string Get(string name)=>Columns.TryGetValue(name,out var col)?Cell(col):null;
        public string Address(string name)=>Sheet+"!"+(Columns.TryGetValue(name,out var col)?col:name)+Number;
        public InvalidDataException Error(string name,string message)=>new InvalidDataException(Address(name)+": "+message);
        public string Required(string name)=>Get(name)??throw Error(name,"必須値が空欄です");
        public int Int(string name,int minimum=int.MinValue,int maximum=int.MaxValue)
        {
            if(!int.TryParse(Required(name),NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)||value<minimum||value>maximum)
                throw Error(name,$"整数 {minimum}〜{maximum} が必要です");
            return value;
        }
        public float Float(string name,float minimum=float.MinValue,float maximum=float.MaxValue)
        {
            if(!float.TryParse(Required(name),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||float.IsNaN(value)||float.IsInfinity(value)||value<minimum||value>maximum)
                throw Error(name,$"有限の数値 {minimum}〜{maximum} が必要です");
            return value;
        }
        public bool Bool(string name)
        {
            var text=Required(name);if(text=="0"||text=="false"||text=="FALSE")return false;
            if(text=="1"||text=="true"||text=="TRUE")return true;
            throw Error(name,"0/1 または true/false が必要です");
        }
    }
    public readonly Dictionary<string,List<Row>> Sheets=new Dictionary<string,List<Row>>(StringComparer.Ordinal);
    public readonly Dictionary<string,List<Row>> RawSheets=new Dictionary<string,List<Row>>(StringComparer.Ordinal);
    public bool Date1904;
    public List<Row> Table(string sheet)=>Sheets.TryGetValue(sheet,out var rows)?rows:throw new InvalidDataException("必須シートがありません: "+sheet);
    public Dictionary<string,Row> Index(string sheet)
    {
        var result=new Dictionary<string,Row>(StringComparer.Ordinal);
        foreach(var row in Table(sheet))
        {
            string id=row.Required("id");
            if(result.ContainsKey(id))throw row.Error("id","IDが重複しています: "+id);
            result.Add(id,row);
        }
        return result;
    }
    static XDocument ReadXml(ZipArchiveEntry entry)
    {
        if(entry==null||entry.Length>16000000)throw new InvalidDataException("XLSXの構造またはサイズが不正です");
        using(var stream=entry.Open())
        using(var reader=XmlReader.Create(stream,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null }))
            return XDocument.Load(reader);
    }
    // rPh contains pronunciation, not cell text. Never concatenate it with Japanese names.
    static string Text(XElement value)=>value==null?null:string.Concat(value.Elements().Where(e=>e.Name==S+"t"||e.Name==S+"r")
        .Select(e=>e.Name==S+"t"?e.Value:string.Concat(e.Elements(S+"t").Select(t=>t.Value))));
    static string Value(XElement cell,string[] shared,bool ignoreFormula=false)
    {
        if(cell==null)return null;
        if(!ignoreFormula&&cell.Element(S+"f")!=null&&(cell.Element(S+"v")==null||
            (string.IsNullOrEmpty(cell.Element(S+"v").Value)&&(string)cell.Attribute("t")!="str")))
            throw new InvalidDataException((string)cell.Attribute("r")+": 数式の計算結果が保存されていません。Excelで再計算して保存してください");
        string value=cell.Element(S+"v")?.Value;
        if((string)cell.Attribute("t")=="s")
        {
            if(!int.TryParse(value,out int index)||index<0||index>=shared.Length)throw new InvalidDataException("不正な共有文字列です");
            value=shared[index];
        }
        else if((string)cell.Attribute("t")=="inlineStr")value=Text(cell.Element(S+"is"));
        else if((string)cell.Attribute("t")=="e")throw new InvalidDataException((string)cell.Attribute("r")+": Excelのエラー値です");
        return string.IsNullOrWhiteSpace(value)?null:value.Trim();
    }
    public static MasterWorkbook Read(string path)
    {
        var result=new MasterWorkbook();
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
        {
            var document=ReadXml(zip.GetEntry("xl/workbook.xml"));
            string date1904=(string)document.Root.Element(S+"workbookPr")?.Attribute("date1904");
            result.Date1904=date1904=="1"||string.Equals(date1904,"true",StringComparison.OrdinalIgnoreCase);
            var sharedEntry=zip.GetEntry("xl/sharedStrings.xml");
            var shared=sharedEntry==null?Array.Empty<string>():ReadXml(sharedEntry).Root.Elements(S+"si").Select(Text).ToArray();
            var relationships=ReadXml(zip.GetEntry("xl/_rels/workbook.xml.rels")).Root.Elements().ToDictionary(e=>(string)e.Attribute("Id"),e=>(string)e.Attribute("Target"));
            foreach(var sheet in document.Root.Element(S+"sheets").Elements(S+"sheet"))
            {
                string name=(string)sheet.Attribute("name"),target=relationships[(string)sheet.Attribute(R+"id")];
                if(target.Contains("..")||target.Contains(":")||target.Contains("\\"))throw new InvalidDataException("不正なシート参照です");
                target=target.StartsWith("/")?target.TrimStart('/'):"xl/"+target;
                var raw=new List<Row>();var data=new List<Row>();var columns=new Dictionary<string,string>(StringComparer.Ordinal);
                bool isTable=name!="基本構成"&&name!="参照テーブル";
                foreach(var element in ReadXml(zip.GetEntry(target)).Descendants(S+"sheetData").Elements(S+"row"))
                {
                    int number=int.Parse((string)element.Attribute("r"),CultureInfo.InvariantCulture);
                    var cells=element.Elements(S+"c").ToArray();
                    string marker=Value(cells.FirstOrDefault(c=>(string)c.Attribute("r")=="A"+number),shared,true);
                    if(marker=="#END")break;
                    if(marker!=null&&marker.StartsWith("#",StringComparison.Ordinal))continue;
                    var row=new Row { Sheet=name,Number=number };
                    foreach(var c in cells)
                    {
                        string address=(string)c.Attribute("r");
                        string col=new string((address??"").TakeWhile(char.IsLetter).ToArray());
                        if(col.Length==0)throw new InvalidDataException(name+": セル番地がありません");
                        var value=Value(c,shared);if(value!=null)row.Cells.Add(col,value);
                    }
                    if(row.Cells.Count==0)continue;
                    raw.Add(row);
                    if(!isTable)continue;
                    if(number==2)
                    {
                        foreach(var cell in row.Cells.Where(c=>c.Key!="A"))
                        {
                            if(columns.ContainsKey(cell.Value))throw row.Error(cell.Key,"カラム名が重複しています");
                            columns.Add(cell.Value,cell.Key);
                        }
                        // This supplied sheet has labels, but deliberately no technical names in F/G.
                        if(name=="アイテム"){columns["minimum"]="F";columns["maximum"]="G";}
                    }
                    if(number<=3)continue;
                    foreach(var column in columns)row.Columns.Add(column.Key,column.Value);
                    if(row.Get("id")==null)throw row.Error("id","データ行のIDが空欄です（未使用行はA列に#を入力）");
                    data.Add(row);
                }
                result.RawSheets.Add(name,raw);
                if(isTable){result.Sheets.Add(name,data);result.Index(name);}
            }
        }
        return result;
    }
    public string Date(Row row,string field)
    {
        string value=row.Get(field);if(value==null)return null;
        if(double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double serial))
        {
            try{return DateTime.FromOADate(serial+(Date1904?1462:0)).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF",CultureInfo.InvariantCulture).TrimEnd('.');}
            catch(ArgumentException){throw row.Error(field,"Excel日時の範囲外です");}
        }
        if(DateTime.TryParseExact(value,new[]{"yyyy-MM-dd","yyyy-MM-ddTHH:mm:ss","yyyy/MM/dd HH:mm:ss"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
            return date.ToString("yyyy-MM-ddTHH:mm:ss",CultureInfo.InvariantCulture);
        throw row.Error(field,"日時形式が不正です");
    }
}
