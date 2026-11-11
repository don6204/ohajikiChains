using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Ohajiki.Tests
{
    public sealed class QuestTests
    {
        HubConfig config;BattleTuning tuning;QuestCatalog quests;PlayerSession session;
        [SetUp] public void Setup()
        {
            tuning=ScriptableObject.CreateInstance<BattleTuning>();config=ScriptableObject.CreateInstance<HubConfig>();
            config.catalog=Enumerable.Range(0,18).Select(i=>{
                var s=ScriptableObject.CreateInstance<SpiritDefinition>();s.displayName="Spirit "+i;s.element=(Element)(i%3);return s;
            }).ToArray();
            config.startingGems=100;config.banners=Array.Empty<GachaBanner>();
            tuning.playerDeck=config.catalog.Take(12).ToArray();tuning.enemyDeck=config.catalog.Take(12).ToArray();
            quests=ScriptableObject.CreateInstance<QuestCatalog>();quests.quests=QuestPrototypeData.CreateDefaults(config.catalog.Length);
            config.quests=quests;session=new PlayerSession(config,tuning,()=>new DateTimeOffset(2026,11,11,0,0,0,TimeSpan.Zero));
        }
        [TearDown] public void Cleanup()
        {
            foreach(var s in config.catalog)UnityEngine.Object.DestroyImmediate(s);
            UnityEngine.Object.DestroyImmediate(quests);UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(tuning);
        }
        QuestRun Begin(string id)
        {
            if(session.Energy<quests.Find(id).energyCost)Assert.IsTrue(session.TryRefillEnergy(out _));
            Assert.IsTrue(session.TryBeginQuest(id,out var run,out string error),error);return run;
        }
        [Test] public void RequestedContentCountsAndDifficultySetsAreExact()
        {
            Assert.IsTrue(quests.Validate(config.catalog.Length,out string error),error);
            Assert.AreEqual(3,quests.quests.Count(q=>q.category==QuestCategory.Event));
            var groups=quests.quests.Where(q=>q.category==QuestCategory.Showdown).GroupBy(q=>q.groupId).ToArray();
            Assert.AreEqual(3,groups.Length);
            foreach(var group in groups)CollectionAssert.AreEquivalent(new[]{QuestDifficulty.Easy,QuestDifficulty.Normal,QuestDifficulty.Hard},group.Select(q=>q.difficulty));
            Assert.AreEqual(5,quests.quests.Count(q=>q.category==QuestCategory.Challenge));
        }
        [Test] public void TowerUnlocksInOrderAndScalesActualEnemyStatsAndFirstRewards()
        {
            int hp=0,attack=0,gems=0;
            for(int i=1;i<=5;i++)
            {
                string id="challenge_0"+i;Assert.IsTrue(session.QuestAvailable(id,out _));
                if(i<5)Assert.IsFalse(session.QuestAvailable("challenge_0"+(i+1),out _));
                var q=quests.Find(id);
                using(var loadout=new BattleLoadout(session,tuning,q))
                {
                    int nextHp=BattleRules.TotalHp(loadout.Tuning.enemyDeck),nextAttack=loadout.Tuning.enemyDeck.Sum(s=>s.Attack);
                    Assert.Greater(nextHp,hp);Assert.Greater(nextAttack,attack);Assert.Greater(q.firstClearGems,gems);
                    hp=nextHp;attack=nextAttack;gems=q.firstClearGems;
                }
                session.CompleteQuest(Begin(id),true,tuning);
            }
        }
        [Test] public void FirstClearAndCompletionAreIdempotentAndReplayGivesOnlyRepeatRewards()
        {
            var run=Begin("showdown_01_easy");int gems=session.Gems;long xp=session.Progress.TotalExperience;
            var first=session.CompleteQuest(run,true,tuning);
            Assert.IsTrue(first.FirstClear);Assert.AreEqual(1,session.Copies(12));Assert.AreEqual(gems+1,session.Gems);
            Assert.IsNull(session.CompleteQuest(run,true,tuning));Assert.AreEqual(1,session.Copies(12));
            Assert.AreEqual(xp+run.Quest.experience,session.Progress.TotalExperience);
            quests.Find(run.Quest.id).spiritDropChance=0;
            var again=session.CompleteQuest(Begin(run.Quest.id),true,tuning);
            Assert.IsFalse(again.FirstClear);Assert.AreEqual(gems+1,session.Gems);Assert.AreEqual(1,session.Copies(12));
        }
        [Test] public void RunSnapshotsRewardsParticipantsAndCannotSpendEntryTwice()
        {
            int energy=session.Energy;var run=Begin("main_01");
            Assert.IsFalse(session.TryBeginQuest("event_01",out _,out _));Assert.AreEqual(energy-1,session.Energy);
            quests.Find("main_01").firstClearGems=999;session.SwitchDeck(1);
            int gems=session.Gems;session.CompleteQuest(run,true,tuning);Assert.AreEqual(gems+1,session.Gems);
            Assert.AreEqual(2,session.Level(0));Assert.AreEqual(1,session.Level(12));
        }
        [Test] public void DefeatAndAbandonDoNotRewardOrUnlock()
        {
            long xp=session.Progress.TotalExperience,coins=session.Progress.Coins;int gems=session.Gems;
            var run=Begin("challenge_01");session.CompleteQuest(run,false,tuning);
            Assert.IsFalse(session.IsCleared(run.Quest.id));Assert.IsFalse(session.QuestAvailable("challenge_02",out _));
            run=Begin("event_01");session.AbandonQuest(run);Assert.IsNull(session.CompleteQuest(run,true,tuning));
            Assert.AreEqual(xp,session.Progress.TotalExperience);Assert.AreEqual(coins,session.Progress.Coins);Assert.AreEqual(gems,session.Gems);
        }
        [Test] public void CalendarUsesJapanTimeAndFailedEntryDoesNotSpend()
        {
            var q=quests.Find("event_01");q.availableFrom="2026-11-12";
            int energy=session.Energy;Assert.IsFalse(session.TryBeginQuest(q.id,out _,out _));Assert.AreEqual(energy,session.Energy);
            session=new PlayerSession(config,tuning,()=>new DateTimeOffset(2026,11,11,15,0,0,TimeSpan.Zero));
            Assert.IsTrue(session.QuestAvailable(q.id,out _));q.availableUntil="2026-11-13";
            session=new PlayerSession(config,tuning,()=>new DateTimeOffset(2026,11,12,15,0,0,TimeSpan.Zero));
            Assert.IsFalse(session.QuestAvailable(q.id,out _));
        }
        [Test] public void RefillChargesExactlyOneGemOnlyWhenNeededAndRankRaisesCapacity()
        {
            int gems=session.Gems;Assert.IsFalse(session.TryRefillEnergy(out _));Assert.AreEqual(gems,session.Gems);
            session.TryEnterBattle();Assert.IsTrue(session.TryRefillEnergy(out _));Assert.AreEqual(gems-1,session.Gems);
            Assert.IsFalse(session.TryRefillEnergy(out _));Assert.AreEqual(gems-1,session.Gems);
            session.Progress.Award(100,0,tuning);Assert.AreEqual(6,session.MaximumEnergy);Assert.AreEqual(11,session.FriendCapacity);
            Assert.IsTrue(session.TryRefillEnergy(out _));Assert.AreEqual(6,session.Energy);
            Assert.IsTrue(session.ApplyShop(ShopCurrency.Gems,session.Gems,Array.Empty<ShopReward>(),out _));session.TryEnterBattle();
            Assert.IsFalse(session.TryRefillEnergy(out _));Assert.AreEqual(5,session.Energy);
        }
        [Test] public void QuestLoadoutsOwnEnemiesAndNeverChangeSourceAssets()
        {
            var q=quests.Find("showdown_02_hard");int hp=tuning.enemyDeck[1].Hp;
            using(var loadout=new BattleLoadout(session,tuning,q))
            {
                Assert.IsTrue(loadout.Tuning.enemyDeck.All(e=>e.element==Element.Water));
                Assert.Greater(loadout.Tuning.enemyDeck[0].Hp,hp);
                Assert.AreNotSame(tuning.enemyDeck[1],loadout.Tuning.enemyDeck[0]);
            }
            Assert.AreEqual(hp,tuning.enemyDeck[1].Hp);
        }
        [Test] public void PracticeKeepsResolvingAfterZeroHpWhileNormalBattleStops()
        {
            var normal=new BattleState(tuning);normal.Enemy.Receive(normal.Enemy.MaxHp);Assert.IsTrue(normal.Finished);
            var practice=new BattleState(tuning,true);practice.Enemy.Receive(practice.Enemy.MaxHp);
            Assert.IsFalse(practice.Finished);Assert.IsTrue(practice.Won);
            practice.Resolve(false,tuning.playerDeck[0],10,3,tuning);practice.ContinuePractice();
            Assert.IsFalse(practice.Finished);Assert.IsFalse(practice.EnemyActsNext);Assert.AreEqual(2,practice.Turn);
            practice.Resolve(false,tuning.playerDeck[1],4,1,tuning);practice.ContinuePractice();Assert.AreEqual(0,practice.Enemy.Hp);
            practice.Player.Receive(practice.Player.MaxHp);Assert.IsTrue(practice.Finished);Assert.IsFalse(practice.Won);
        }
        [Test] public void InvalidQuestReferencesCyclesDatesAndNumbersFailValidation()
        {
            var q=quests.Find("main_01");q.prerequisiteId="main_02";Assert.IsFalse(quests.Validate(18,out _));q.prerequisiteId="";
            q.availableFrom="invalid";Assert.IsFalse(quests.Validate(18,out _));q.availableFrom="";
            q.hpMultiplier=float.NaN;Assert.IsFalse(quests.Validate(18,out _));q.hpMultiplier=1;
            q.rewardSpiritIndex=18;Assert.IsFalse(quests.Validate(18,out _));q.rewardSpiritIndex=-1;
            q.spiritDropChance=.2f;Assert.IsFalse(quests.Validate(18,out _));
        }
        [Test] public void AllRewardTypesReachTheSharedInventory()
        {
            var q=quests.Find("event_01");q.shiningStones=2;q.materialCount=3;q.materialType=1;
            int stones=session.ShiningStones,power=session.MaterialCount(1);var run=Begin(q.id);
            session.CompleteQuest(run,true,tuning);
            Assert.AreEqual(30,session.EventPoints);Assert.AreEqual(stones+2,session.ShiningStones);Assert.AreEqual(power+3,session.MaterialCount(1));
        }
        [Test] public void ImportValuesPreserveZeroAndRejectInvalidTypesAndProtectedIds()
        {
            var q=quests.Find("main_01");DataInputImporter.ApplyValue(q,"medals","0");Assert.AreEqual(0,q.medals);
            DataInputImporter.ApplyValue(q,"enemyElement","Water");Assert.AreEqual(Element.Water,q.enemyElement);
            DataInputImporter.ApplyValue(tuning,"effectThresholds","1;3;5;10");CollectionAssert.AreEqual(new[]{1,3,5,10},tuning.effectThresholds);
            Assert.Throws<InvalidDataException>(()=>DataInputImporter.ApplyValue(q,"id","rename"));
            Assert.Throws<InvalidDataException>(()=>DataInputImporter.ApplyValue(q,"hpMultiplier","NaN"));
            Assert.Throws<InvalidDataException>(()=>DataInputImporter.ApplyValue(q,"enemyDeck","0"));
            Assert.Throws<InvalidDataException>(()=>DataInputImporter.ApplyValue(q,"unknown","3"));
        }
        [TestCase(false)] [TestCase(true)]
        public void XlsxReaderHandlesEmptyWorkbookAndInlineRowsWithoutEditingIt(bool populated)
        {
            string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
            try
            {
                using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
                using(var writer=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open()))
                {
                    string Cell(string id,string value)=>$"<c r='{id}' t='inlineStr'><is><t>{value}</t></is></c>";
                    string rows=populated?"<row r='1'>"+Cell("A1","対象")+Cell("B1","ID")+Cell("C1","項目")+Cell("D1","値")+"</row>"+
                        "<row r='2'>"+Cell("A2","quest")+Cell("B2","event_01")+Cell("C2","medals")+Cell("D2","0")+"</row>"+
                        "<row r='3'>"+Cell("A3","quest")+Cell("B3","event_01")+Cell("C3","experience")+"</row>":"";
                    writer.Write("<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData>"+rows+"</sheetData></worksheet>");
                }
                byte[] before=File.ReadAllBytes(path);var rowsRead=DataInputImporter.ReadRows(path);
                Assert.AreEqual(populated?1:0,rowsRead.Count);if(populated)Assert.AreEqual("0",rowsRead[0].value);
                CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
            }
            finally{File.Delete(path);}
        }
        [Test] public void StructuredMasterIsPreservedAndCannotBeImportedAsLegacyOverrides()
        {
            string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
            int medals=quests.Find("event_01").medals;
            try
            {
                using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
                using(var writer=new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open()))
                    writer.Write("<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheets>"+
                        "<sheet name='00_入力ガイド'/><sheet name='05_クエスト'/><sheet name='17_取込定義'/></sheets></workbook>");
                byte[] before=File.ReadAllBytes(path);
                Assert.IsTrue(DataInputImporter.IsStructuredMaster(path));
                StringAssert.Contains("import not connected",DataInputImporter.PrepareBuildData(path));
                Assert.Throws<InvalidDataException>(()=>DataInputImporter.Import(path,config,tuning));
                Assert.AreEqual(medals,quests.Find("event_01").medals);
                CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
            }
            finally{File.Delete(path);}
        }
        [TestCase(false)] [TestCase(true)]
        public void XlsxImportCommitsValidRowsTogetherAndInvalidRowsLeaveEveryOriginalUnchanged(bool invalid)
        {
            string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
            int medals=quests.Find("event_01").medals,refill=config.energyRefillGems;
            try
            {
                using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
                using(var writer=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open()))
                {
                    string Cell(string id,string value)=>$"<c r='{id}' t='inlineStr'><is><t>{value}</t></is></c>";
                    writer.Write("<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData>"+
                        "<row r='1'>"+Cell("A1","対象")+Cell("B1","ID")+Cell("C1","項目")+Cell("D1","値")+"</row>"+
                        "<row r='2'>"+Cell("A2","quest")+Cell("B2","event_01")+Cell("C2","medals")+Cell("D2","0")+"</row>"+
                        "<row r='3'>"+Cell("A3","Hub")+Cell("C3","energyRefillGems")+Cell("D3",invalid?"-1":"2")+"</row>"+
                        "</sheetData></worksheet>");
                }
                byte[] before=File.ReadAllBytes(path);
                if(invalid)
                {
                    Assert.Throws<InvalidDataException>(()=>DataInputImporter.Import(path,config,tuning));
                    Assert.AreEqual(medals,quests.Find("event_01").medals);Assert.AreEqual(refill,config.energyRefillGems);
                }
                else
                {
                    Assert.AreEqual(2,DataInputImporter.Import(path,config,tuning));
                    Assert.AreEqual(0,quests.Find("event_01").medals);Assert.AreEqual(2,config.energyRefillGems);
                    Assert.AreSame(quests,config.quests);
                }
                CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
            }
            finally{File.Delete(path);}
        }
    }
}
