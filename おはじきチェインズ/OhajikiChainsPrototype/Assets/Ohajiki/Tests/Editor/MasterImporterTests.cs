using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using Ohajiki;
using UnityEditor;

namespace Ohajiki.Tests
{
    public sealed class MasterImporterTests
    {
        string temporary;
        [TearDown] public void Cleanup(){if(temporary!=null&&File.Exists(temporary))File.Delete(temporary);}
        string Workbook(string rows,string shared=null,string properties="")
        {
            temporary=Path.Combine(Path.GetTempPath(),"ohajiki-master-"+Guid.NewGuid().ToString("N")+".xlsx");
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Create))
            {
                void Entry(string name,string text){using(var writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write(text);}
                Entry("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"+properties+"<sheets><sheet name=\"テスト\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Entry("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/custom.xml\"/></Relationships>");
                Entry("xl/worksheets/custom.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"+rows+"</sheetData></worksheet>");
                if(shared!=null)Entry("xl/sharedStrings.xml","<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"+shared+"</sst>");
            }
            return temporary;
        }
        static string Text(string address,string value)=>"<c r=\""+address+"\" t=\"inlineStr\"><is><t>"+value+"</t></is></c>";
        static string Header=>"<row r=\"2\">"+Text("A2","カラム")+Text("B2","id")+Text("C2","name")+"</row>";
        [Test] public void EndAndCommentRowsIgnoreTheirInvalidPayloadBeforeCellParsing()
        {
            string path=Workbook(Header+"<row r=\"4\">"+Text("A4","# comment")+"<c r=\"B4\"><f>INVALID()</f></c></row>"+
                "<row r=\"5\">"+Text("B5","only")+Text("C5","採用")+"</row>"+
                "<row r=\"6\">"+Text("A6"," #END ")+"<c r=\"B6\" t=\"e\"><v>#REF!</v></c></row>"+
                "<row r=\"7\">"+Text("B7","only")+"<c r=\"C7\"><f>BAD()</f></c></row>");
            var book=MasterWorkbook.Read(path);Assert.AreEqual(1,book.Table("テスト").Count);
            Assert.AreEqual("採用",book.Table("テスト")[0].Get("name"));
        }
        [Test] public void SharedRichTextExcludesJapanesePhonetics()
        {
            string path=Workbook(Header+"<row r=\"4\">"+Text("B4","id")+"<c r=\"C4\" t=\"s\"><v>0</v></c></row>",
                "<si><r><t>炎</t></r><r><t>王</t></r><rPh sb=\"0\" eb=\"2\"><t>エンオウ</t></rPh></si>");
            Assert.AreEqual("炎王",MasterWorkbook.Read(path).Table("テスト")[0].Get("name"));
        }
        [Test] public void NonCommentRowsRequireIdsAndDuplicateIdsAreRejected()
        {
            string path=Workbook(Header+"<row r=\"4\">"+Text("C4","名前だけ")+"</row>");
            StringAssert.Contains("B4",Assert.Throws<InvalidDataException>(()=>MasterWorkbook.Read(path)).Message);
            File.Delete(path);temporary=null;
            path=Workbook(Header+"<row r=\"4\">"+Text("B4","same")+"</row><row r=\"5\">"+Text("B5","same")+"</row>");
            StringAssert.Contains("重複",Assert.Throws<InvalidDataException>(()=>MasterWorkbook.Read(path)).Message);
        }
        [Test] public void FormulaWithoutSavedResultIsRejected()
        {
            string path=Workbook(Header+"<row r=\"4\">"+Text("B4","id")+"<c r=\"C4\"><f>1+1</f></c></row>");
            StringAssert.Contains("数式",Assert.Throws<InvalidDataException>(()=>MasterWorkbook.Read(path)).Message);
        }
        [Test] public void FormulaUsesTheSavedExcelCalculationWithoutEditingTheFile()
        {
            string path=Workbook(Header+"<row r=\"4\">"+Text("B4","id")+"<c r=\"C4\"><f>100+20</f><v>120</v></c></row>");
            string hash=MasterDataImporter.Hash(path);
            Assert.AreEqual("120",MasterWorkbook.Read(path).Table("テスト")[0].Get("name"));
            Assert.AreEqual(hash,MasterDataImporter.Hash(path));
        }
        [TestCase("1")][TestCase("true")]
        public void Excel1904BooleanAndTimeAreDecoded(string flag)
        {
            string path=Workbook(Header+"<row r=\"4\">"+Text("B4","id")+"<c r=\"C4\"><v>0.5</v></c></row>",null,"<workbookPr date1904=\""+flag+"\"/>");
            var book=MasterWorkbook.Read(path);Assert.AreEqual("1904-01-01T12:00:00",book.Date(book.Table("テスト")[0],"name"));
        }
        [Test] public void CurrentMasterStagesAllReferencesAndPreservesSourceAndPrototypeAssets()
        {
            string source=MasterDataImporter.DefaultPath,hash=MasterDataImporter.Hash(source);
            var original=AssetDatabase.LoadAssetAtPath<HubConfig>("Assets/Ohajiki/HubConfig.asset");
            string oldName=original.catalog[0].displayName;var oldPortrait=original.catalog[0].portrait;
            using(var stage=MasterDataImporter.Prepare(source))
            {
                var d=stage.Data;Assert.AreEqual(24,d.home.catalog.Length);Assert.AreEqual(25,d.npcs.Length);
                Assert.AreEqual(25,d.home.quests.quests.Length);Assert.AreEqual(4,d.home.banners.Length);
                Assert.IsTrue(d.home.catalog.All(s=>s.portrait==null));
                CollectionAssert.AreEqual(new[]{100f,120f,140f,160f,180f},d.home.catalog[0].skillPower);
                CollectionAssert.AreEqual(new[]{.9f,1.1f,1.3f,1.5f,1.7f},d.home.catalog[8].skillPower);
                Assert.IsTrue(d.home.catalog[7].IsValid);Assert.AreEqual(0,d.home.catalog[7].Power(5));
                Assert.IsTrue(d.npcs.All(n=>n.deck.Length==12));
                Assert.IsTrue(d.npcs.Single(n=>n.id=="NPC0000_0003").deck.All(s=>s.level==50));
                Assert.AreEqual(22,d.battle.chargeRadius);Assert.AreEqual(8,d.battle.boardRows);
                Assert.AreEqual(Element.Water,d.home.catalog[13].element);Assert.AreEqual(770,d.home.catalog[13].baseHp);
                Assert.AreEqual(10,d.home.maximumEnergy);Assert.AreEqual(300,d.home.energyRecoverySeconds);
                var workbook=MasterWorkbook.Read(source);
                float growth=workbook.Table("定数").Single(r=>r.Required("id")=="BattleTuning.contactAttackFactor").Float("value_float",0);
                Assert.AreEqual(growth,d.battle.contactAttackFactor,"contact growth follows the current master value");
                var sourceSpirits=workbook.Index("スピリター");
                foreach(var spirit in d.home.catalog)
                    Assert.AreEqual(sourceSpirits[spirit.masterId].Int("atk",0),spirit.baseAttack,"attack imported for "+spirit.masterId);
                Assert.AreEqual(500,d.battle.damageSizeStep);Assert.AreEqual(.1f,d.battle.damageSizeIncrement);Assert.AreEqual(1.5f,d.battle.damageSizeMaximum);
                Assert.IsTrue(d.home.quests.quests.All(q=>q.useMasterRewards));
                Assert.AreEqual(120,d.home.quests.Find("QUEST0000_0000_LV0").experience);
                Assert.AreEqual(1.0/24,GachaRules.Probability(d.home,d.home.banners[0],0),1e-10);
                Assert.AreNotSame(original,d.home);Assert.AreEqual(hash,d.sourceHash);
            }
            Assert.AreEqual(hash,MasterDataImporter.Hash(source));Assert.AreEqual(oldName,original.catalog[0].displayName);Assert.AreSame(oldPortrait,original.catalog[0].portrait);
        }
    }
}
