using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class MasterImportFailureTests
    {
        static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string temporary;
        [TearDown] public void Cleanup()
        {
            if(temporary!=null && File.Exists(temporary))File.Delete(temporary);
        }
        static XDocument Read(ZipArchiveEntry entry)
        {
            using(var stream=entry.Open())return XDocument.Load(stream);
        }
        static Dictionary<string,string> FileHashes(string source)
        {
            // Include the published snapshot even on a first import, when it should remain absent.
            return Directory.GetFiles("Assets/Ohajiki","*.asset",SearchOption.AllDirectories)
                .Concat(new[]{source,MasterDataImporter.OutputPath}).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(path=>path,path=>File.Exists(path)?MasterDataImporter.Hash(path):null,StringComparer.OrdinalIgnoreCase);
        }
        static Dictionary<UnityEngine.Object,string> PrototypeState()
        {
            var home=AssetDatabase.LoadAssetAtPath<HubConfig>("Assets/Ohajiki/HubConfig.asset");
            var battle=AssetDatabase.LoadAssetAtPath<BattleTuning>("Assets/Ohajiki/BattleTuning.asset");
            Assert.NotNull(home);Assert.NotNull(battle);
            return new UnityEngine.Object[]{home,battle,home.shop,home.quests}.Concat(home.catalog)
                .Concat(battle.enemyDeck).Where(obj=>obj!=null).Distinct()
                .ToDictionary(obj=>obj,obj=>EditorJsonUtility.ToJson(obj));
        }
        void ChangeCopyCell(string sheetName,string address,string replacement)
        {
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Update))
            {
                var workbook=Read(zip.GetEntry("xl/workbook.xml"));
                var sheet=workbook.Descendants(S+"sheet").Single(e=>(string)e.Attribute("name")==sheetName);
                var rel=Read(zip.GetEntry("xl/_rels/workbook.xml.rels")).Root.Elements()
                    .Single(e=>(string)e.Attribute("Id")== (string)sheet.Attribute(R+"id"));
                string target=(string)rel.Attribute("Target");
                target=target.StartsWith("/",StringComparison.Ordinal)?target.TrimStart('/'):"xl/"+target;
                var entry=zip.GetEntry(target);Assert.NotNull(entry);
                var document=Read(entry);
                var cell=document.Descendants(S+"c").Single(c=>(string)c.Attribute("r")==address);
                if(replacement==null)cell.Remove();
                else
                {
                    cell.RemoveNodes();cell.SetAttributeValue("t","inlineStr");
                    cell.Add(new XElement(S+"is",new XElement(S+"t",replacement)));
                }
                entry.Delete();
                using(var stream=zip.CreateEntry(target).Open())document.Save(stream);
            }
        }
        [TestCase("NPCデッキ","sp_id","MISSING_SPIRIT_REFERENCE","参照先")]
        [TestCase("スピリター","hp",null,"必須値")]
        public void InvalidInputRejectsTheStageWithoutChangingAnySourceOrPublishedData(string sheet,string field,string replacement,string reason)
        {
            string source=MasterDataImporter.DefaultPath;
            var filesBefore=FileHashes(source);var objectsBefore=PrototypeState();
            var row=MasterWorkbook.Read(source).Index(sheet).Values.OrderBy(r=>r.Number).First();
            string column=row.Columns[field],address=column+row.Number;
            temporary=Path.Combine(Path.GetTempPath(),"ohajiki-master-reject-"+Guid.NewGuid().ToString("N")+".xlsx");
            File.Copy(source,temporary);
            ChangeCopyCell(sheet,address,replacement);
            string temporaryHash=MasterDataImporter.Hash(temporary);
            var error=Assert.Throws<InvalidDataException>(()=>{
                using(var stage=MasterDataImporter.Prepare(temporary)){}
            });
            StringAssert.Contains(sheet+"!"+address,error.Message);StringAssert.Contains(reason,error.Message);
            Assert.AreEqual(temporaryHash,MasterDataImporter.Hash(temporary),"Even rejected input is read-only");
            var filesAfter=FileHashes(source);
            CollectionAssert.AreEquivalent(filesBefore.Keys,filesAfter.Keys,"No asset file may be added or removed");
            foreach(var file in filesBefore)Assert.AreEqual(file.Value,filesAfter[file.Key],file.Key);
            foreach(var state in objectsBefore)Assert.AreEqual(state.Value,EditorJsonUtility.ToJson(state.Key),state.Key.name);
        }
    }
}
