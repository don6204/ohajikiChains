#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Ohajiki;

using Ohajiki.Portable;
using Object=Ohajiki.Portable.Object;

public static class MasterDataImporter
{
    const string Root="Assets/Ohajiki/";
    public const string OutputPath=Root+"Resources/OhajikiMasterData.asset";
    public static Func<string, Sprite> ResolveTexture = _ => null;
    public sealed class Stage : IDisposable
    {
        public MasterGameData Data;
        public readonly List<ScriptableObject> Objects=new List<ScriptableObject>();
        internal bool committed = false;
        public T Add<T>(T value,string name) where T:ScriptableObject {value.name=name;Objects.Add(value);return value;}
        public void Dispose(){if(!committed)foreach(var item in Objects)if(item!=null)Object.DestroyImmediate(item);}
    }
    static readonly Dictionary<string,Skill> Skills=new Dictionary<string,Skill> {
        {"NSK_ATTACK_FIXED",Skill.Attack},{"NSK_ATTACK_RATE",Skill.PercentAttack},
        {"NSK_HEEL_FIXED",Skill.Heal},{"NSK_HEEL_RATE",Skill.PercentHeal},
        {"NSK_DEFENSE_FIXED",Skill.Guard},{"NSK_DEFENSE_RATE",Skill.PercentGuard},
        {"NSK_ATTACK_UP",Skill.AttackUp},{"NSK_SPEED_UP",Skill.SpeedUp},{"NSK_CLEANSE",Skill.Cleanse},
        {"NSK_ATTACK_DOWN",Skill.AttackDown},{"NSK_SPEED_DOWN",Skill.SpeedDown}
    };
    static readonly Dictionary<string,ShopRewardKind> Items=new Dictionary<string,ShopRewardKind> {
        {"ITEM0000_0000",ShopRewardKind.Medals},{"ITEM0000_0001",ShopRewardKind.Gems},{"ITEM0000_0002",ShopRewardKind.Gems},
        {"ITEM0000_0003",ShopRewardKind.FirePower},{"ITEM0000_0004",ShopRewardKind.WaterPower},
        {"ITEM0000_0005",ShopRewardKind.WoodPower},{"ITEM0000_0006",ShopRewardKind.AllPower},
        {"ITEM0000_0007",ShopRewardKind.ShiningStones},{"ITEM0000_0008",ShopRewardKind.EventPoints}
    };
    public static string Hash(string path)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
    }
    static T Require<T>(Dictionary<string,T> map,string id,MasterWorkbook.Row row,string field)
    {
        if(id==null||!map.TryGetValue(id,out var value))throw row.Error(field,"参照先がありません: "+id);
        return value;
    }
    static Sprite Texture(MasterWorkbook.Row row,string field)
    {
        string id=row.Get(field);if(id==null)return null;
        var sprite=ResolveTexture(id);
        if(sprite==null)throw row.Error(field,"Spriteが見つかりません: "+id);
        return sprite;
    }
    static int OptionalInt(MasterWorkbook.Row r,string f,int fallback,int min=0,int max=int.MaxValue)=>r.Get(f)==null?fallback:r.Int(f,min,max);
    static float Rate(MasterWorkbook.Row r,string field)=>r.Int(field,0,10000)/10000f;
    public static Stage Prepare(string path,HubConfig prototypeHome=null,BattleTuning prototypeBattle=null)
    {
        string hash=Hash(path);var book=MasterWorkbook.Read(path);
        if(hash!=Hash(path))throw new IOException("読み込み中にMasterData.xlsxが変更されました。保存後に再実行してください。");
        if(prototypeHome==null||prototypeBattle==null)throw new InvalidOperationException("プロトタイプの基本アセットがありません");
        var stage=new Stage();
        try
        {
            var data=stage.Data=stage.Add(ScriptableObject.CreateInstance<MasterGameData>(),"MasterData");
            data.sourceFile=Path.GetFileName(path);data.sourceHash=hash;data.importedUtc=DateTime.UtcNow.ToString("O");
            data.sourceRows=book.Sheets.Values.Sum(rows=>rows.Count);
            data.home=stage.Add(Object.Instantiate(prototypeHome),"Master Home");
            data.battle=stage.Add(Object.Instantiate(prototypeBattle),"Master Battle");
            data.home.shop=prototypeHome.shop==null?null:stage.Add(Object.Instantiate(prototypeHome.shop),"Master Shop");
            data.home.quests=stage.Add(ScriptableObject.CreateInstance<QuestCatalog>(),"Master Quests");
            data.constants=stage.Add(ScriptableObject.CreateInstance<MasterConstants>(),"Master Constants");
            ApplyConstants(book,data);
            var normal=book.Index("ノーマルスキル");var actions=book.Index("アクションスキル");
            var spiritRows=book.Table("スピリター").OrderBy(r=>r.Int("number",1)).ToArray();
            RequireUniqueOrder(spiritRows);
            var spirits=new Dictionary<string,SpiritDefinition>(StringComparer.Ordinal);
            foreach(var row in spiritRows)
            {
                string id=row.Required("id");
                var s=stage.Add(ScriptableObject.CreateInstance<SpiritDefinition>(),id);
                s.masterId=id;s.displayName=row.Required("name");s.description=row.Get("description");s.portrait=Texture(row,"tex_id");
                s.element=(Element)row.Int("type",0,2);s.rarity=row.Int("rarity",1,5);s.level=1;
                s.baseHp=row.Int("hp",1);s.baseAttack=row.Int("atk",0);s.baseSpeed=row.Float("speed",1);
                s.maxLevel=row.Int("lv_max",1,60);s.hpPerLevel=row.Int("up_hp",0);s.attackPerLevel=row.Int("up_atk",0);s.speedPerLevel=row.Float("up_speed",0);
                var definition=Require(normal,row.Required("nsk_id"),row,"nsk_id");
                if(!Skills.TryGetValue(row.Required("nsk_id"),out s.skill))throw row.Error("nsk_id","未対応のスキル処理です");
                int category=row.Int("nsk_category",0,1);
                if(category!=definition.Int("category_id",0,1))throw row.Error("nsk_category","スキル定義とカテゴリが一致しません");
                s.skillName=row.Get("nsk_name");
                s.skillPower=s.skill==Skill.Cleanse?null:Enumerable.Range(1,5).Select(level=>category==0?(float)row.Int("nsk_lv"+level+"_num_fixed",0):row.Float("nsk_lv"+level+"_num_rate",0)).ToArray();
                if(s.skill==Skill.Guard||s.skill==Skill.PercentGuard)s.guardTurns=row.Int("nsk_turn",1);
                if(s.skill==Skill.AttackUp||s.skill==Skill.SpeedUp||s.skill==Skill.AttackDown||s.skill==Skill.SpeedDown)s.statusTurns=row.Int("nsk_turn",1);
                // Blank action ID means no action skill, not an inherited prototype ability.
                s.actionSkill=ActionSkill.None;
                if(row.Get("ask_id")!=null)
                {
                    var action=Require(actions,row.Get("ask_id"),row,"ask_id");int kind=action.Int("category_id",0,1);
                    if(row.Int("ask_category",0,1)!=kind)throw row.Error("ask_category","アクション定義とカテゴリが一致しません");
                    if(kind==0)
                    {
                        s.actionSkill=ActionSkill.Trap;s.trapDamage=row.Int("ask_num_fixed",0);
                        string name=action.Required("name");
                        if(name.StartsWith("火属性",StringComparison.Ordinal))s.trapTargetElement=Element.Fire;
                        else if(name.StartsWith("水属性",StringComparison.Ordinal))s.trapTargetElement=Element.Water;
                        else if(name.StartsWith("木属性",StringComparison.Ordinal))s.trapTargetElement=Element.Wood;
                        else throw action.Error("name","罠の対象属性が未設定です");
                    }
                    else
                    {
                        s.actionSkill=ActionSkill.Split;
                        var match=Regex.Match(action.Required("name"),@"^(\d+)接触で\d+体分裂$");
                        if(!match.Success)throw action.Error("name","分裂が発動する接触数を指定してください");
                        s.splitContactThreshold=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
                        s.splitTotalProjectiles=row.Int("ask_num_fixed",2,12);
                    }
                }
                if(!s.IsValid)throw row.Error("id","キャラ設定が不正です");
                spirits.Add(id,s);
            }
            data.home.catalog=spiritRows.Select(r=>spirits[r.Required("id")]).ToArray();
            data.battle.playerDeck=data.home.catalog.Take(SpiritDeck.Size).ToArray();
            ApplyReferenceTable(book,data);
            data.items=book.Table("アイテム").Select(r=>new MasterItem { id=r.Required("id"),name=r.Required("name"),description=r.Get("description"),
                useDescription=r.Get("use_description"),texture=Texture(r,"item_tex_id"),hasMinimum=r.Get("minimum")!=null,hasMaximum=r.Get("maximum")!=null,
                minimum=OptionalInt(r,"minimum",0),maximum=OptionalInt(r,"maximum",0) }).ToArray();
            foreach(var item in data.items)if(item.hasMinimum&&item.hasMaximum&&item.minimum>item.maximum)throw new InvalidDataException(item.id+": 所持数の上下限が逆です");
            var npcRows=book.Index("NPC");var npcDecks=book.Table("NPCデッキ");
            foreach(var r in npcDecks)Require(npcRows,r.Required("npc_id"),r,"npc_id");
            var npcs=new Dictionary<string,MasterNpc>(StringComparer.Ordinal);
            foreach(var pair in npcRows)
            {
                var row=pair.Value;var slots=npcDecks.Where(r=>r.Required("npc_id")==pair.Key).OrderBy(r=>r.Int("number",1,12)).ToArray();
                if(slots.Length!=SpiritDeck.Size)throw row.Error("id","NPCデッキは12枠必要です");RequireUniqueOrder(slots);
                var npc=new MasterNpc { id=pair.Key,name=row.Required("name"),description=row.Get("description"),mainSpiritId=row.Get("sp_id"),
                    hasType=row.Get("type")!=null,type=OptionalInt(row,"type",0),deck=slots.Select(r=>{
                        var original=Require(spirits,r.Required("sp_id"),r,"sp_id");
                        var enemy=stage.Add(Object.Instantiate(original),r.Required("id"));enemy.level=r.Int("sp_lv",1,original.maxLevel);return enemy;
                    }).ToArray() };
                if(npc.mainSpiritId!=null)Require(spirits,npc.mainSpiritId,row,"sp_id");npcs.Add(pair.Key,npc);
            }
            data.npcs=npcs.Values.ToArray();
            if(data.npcs.Length==0)throw new InvalidDataException("NPC: 敵編成が少なくとも1件必要です");
            data.battle.enemyDeck=data.npcs.First().deck;
            ApplyCharges(book,data.battle);
            ApplyQuests(book,data,npcs,spirits);
            ApplyGacha(book,data,spirits);
            if(!data.home.Validate(out string error)||!data.battle.Validate(out error)||!data.constants.Validate(out error))throw new InvalidDataException(error);
            if(data.constants.entries.Any(e=>e.id=="PlayerProgress.initialRank"&&e.value>data.battle.maxRank))throw new InvalidDataException("初期ランクが最大ランクを超えています");
            if(hash!=Hash(path))throw new IOException("検証中にMasterData.xlsxが変更されました。保存後に再実行してください。");
            return stage;
        }
        catch{stage.Dispose();throw;}
    }
    static void RequireUniqueOrder(MasterWorkbook.Row[] rows)
    {
        if(rows.GroupBy(r=>r.Int("number",1)).Any(g=>g.Count()>1))throw new InvalidDataException(rows[0].Sheet+": 同じグループの番号が重複しています");
    }
    static void ApplyConstants(MasterWorkbook book,MasterGameData data)
    {
        var code=new List<MasterConstants.Entry>();
        foreach(var row in book.Table("定数"))
        {
            string id=row.Required("id");string integer=row.Get("value_int"),floating=row.Get("value_float");
            if((integer==null)==(floating==null))throw row.Error("value_int","整数値または小数値の片方を指定してください");
            if(!double.TryParse(integer??floating,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||double.IsNaN(value)||double.IsInfinity(value))throw row.Error("value_float","有限の数値が必要です");
            if(integer!=null&&(value!=Math.Truncate(value)||value<int.MinValue||value>int.MaxValue))throw row.Error("value_int","int32の整数が必要です");
            object target=null;string field=null;
            if(id.StartsWith("BattleTuning.",StringComparison.Ordinal)){target=data.battle;field=id.Substring(13);}
            else if(id.StartsWith("HubConfig.",StringComparison.Ordinal)){target=data.home;field=id.Substring(10);}
            else if(id.StartsWith("ShopConfig.",StringComparison.Ordinal)){target=data.home.shop;field=id.Substring(11);}
            if(field!=null)
            {
                if(target==null)throw row.Error("id","対象の設定がありません");
                SetNumber(target,field,value,row);
            }
            else
            {
                if(!MasterConstants.Supports(id))throw row.Error("id","未対応の定数です: "+id);
                code.Add(new MasterConstants.Entry {id=id,value=value});
            }
        }
        data.constants.entries=code.ToArray();
        if(!data.constants.Validate(out string error))throw new InvalidDataException(error);
    }
    static void SetNumber(object target,string path,double value,MasterWorkbook.Row row)
    {
        int dot=path.IndexOf('.');string head=dot<0?path:path.Substring(0,dot),tail=dot<0?null:path.Substring(dot+1);
        var match=Regex.Match(head,@"^(\w+)(?:\[(\d+)\])?$");
        var field=match.Success?target.GetType().GetField(match.Groups[1].Value,BindingFlags.Instance|BindingFlags.Public):null;
        if(field==null||field.IsDefined(typeof(HideInInspector),true))throw row.Error("id","未対応の設定項目です: "+path);
        if(match.Groups[2].Success)
        {
            var array=field.GetValue(target) as Array;int index=int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);
            if(array==null||index>=array.Length)throw row.Error("id","配列の範囲外です");
            if(tail==null)array.SetValue(ConvertNumber(array.GetType().GetElementType(),value,row),index);
            else {var item=array.GetValue(index);SetNumber(item,tail,value,row);array.SetValue(item,index);}
        }
        else if(tail!=null)
        {var nested=field.GetValue(target);if(nested==null)throw row.Error("id","設定対象が空欄です");SetNumber(nested,tail,value,row);field.SetValue(target,nested);}
        else
        {
            var min=field.GetCustomAttribute<MinAttribute>();var range=field.GetCustomAttribute<RangeAttribute>();
            if(min!=null&&value<min.min||range!=null&&(value<range.min||value>range.max))throw row.Error("value_int","設定範囲外です: "+path);
            field.SetValue(target,ConvertNumber(field.FieldType,value,row));
        }
    }
    static object ConvertNumber(Type type,double value,MasterWorkbook.Row row)
    {
        if(type==typeof(float)&&value>=-float.MaxValue&&value<=float.MaxValue)return (float)value;
        if(type==typeof(int)&&value==Math.Truncate(value)&&value>=int.MinValue&&value<=int.MaxValue)return (int)value;
        if(type==typeof(bool)&&(value==0||value==1))return value==1;
        throw row.Error("value_int","設定の数値型が不正です");
    }
    static void ApplyReferenceTable(MasterWorkbook book,MasterGameData data)
    {
        if(!book.RawSheets.TryGetValue("参照テーブル",out var rows))throw new InvalidDataException("参照テーブルがありません");
        string Cell(int number,string column)=>rows.FirstOrDefault(r=>r.Number==number)?.Cell(column);
        float Number(int number,string column)
        {
            if(!float.TryParse(Cell(number,column),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException($"参照テーブル!{column}{number}: 数値が必要です");return value;
        }
        data.battle.specialAttackMultiplier=Number(17,"D");data.battle.specialSpeedMultiplier=Number(18,"D");
        float split=Number(19,"D");if(split!=Mathf.Floor(split))throw new InvalidDataException("参照テーブル!D19: 分裂数は整数です");data.battle.specialSplitTotal=(int)split;
        foreach(var spirit in data.home.catalog)
        {
            int row=36+spirit.rarity;float basis=Number(row,"C"),growth=Number(row,"G");
            if(basis<1||growth<0||basis!=Mathf.Floor(basis)||growth!=Mathf.Floor(growth))throw new InvalidDataException("参照テーブル: 育成EXPは正の基準値・非負の上昇値が必要です");
            spirit.experienceBase=(int)basis;spirit.experiencePerLevel=(int)growth;
        }
    }
    static void ApplyCharges(MasterWorkbook book,BattleTuning battle)
    {
        var rows=book.Table("チャージボール").OrderBy(r=>r.Int("number",1,7)).ToArray();
        if(rows.Length!=7)throw new InvalidDataException("チャージボールは通常と特殊6種が必要です");RequireUniqueOrder(rows);
        if(rows.Sum(r=>r.Int("app_rate",0,10000))!=10000)throw new InvalidDataException("チャージボールの出現率合計は10000が必要です");
        battle.specialChargeProbabilities=rows.Skip(1).Select(r=>Rate(r,"app_rate")).ToArray();
        var kinds=book.Index("チャージボール");
        var combinations=new HashSet<string>();
        foreach(var row in book.Table("チャージボール種類"))
        {
            string id=row.Required("exc_id");Require(kinds,id,row,"exc_id");int element=row.Int("type",0,3);
            if(!combinations.Add(id+":"+element))throw row.Error("exc_id","属性と特殊種別の組が重複しています");
        }
        if(combinations.Count!=28)throw new InvalidDataException("チャージボール種類は7種×4属性の28組が必要です");
    }
    static void ApplyQuests(MasterWorkbook book,MasterGameData data,Dictionary<string,MasterNpc> npcs,Dictionary<string,SpiritDefinition> spirits)
    {
        var groups=book.Index("クエスト");var levels=book.Index("クエスト難易度");var items=book.Index("アイテム");
        var rewardRows=book.Table("クエスト報酬");foreach(var row in rewardRows)Require(levels,row.Required("quest_lv_id"),row,"quest_lv_id");
        var quests=new List<QuestDefinition>();
        foreach(var row in levels.Values)
        {
            var group=Require(groups,row.Required("quest_id"),row,"quest_id");var npc=Require(npcs,row.Required("npc_id"),row,"npc_id");
            var q=new QuestDefinition {id=row.Required("id"),groupId=row.Required("quest_id"),title=row.Required("name"),description=group.Get("description"),
                category=(QuestCategory)group.Int("quest_type_id",0,5),difficulty=(QuestDifficulty)row.Int("lv_id",0,3),order=group.Int("number",1),
                energyCost=row.Int("energy_cost",0,1000),enemyDeck=npc.deck,preserveEnemyLevels=true,enemyLevel=npc.deck[0].level,
                enemyElement=npc.deck[0].element,useEnemyPortrait=true,enemyPortrait=npc.mainSpiritId==null?null:spirits[npc.mainSpiritId].portrait,
                banner=Texture(row,"banner_tex_id")??Texture(group,"banner_tex_id"),medals=0,experience=0,useMasterRewards=true};
            // The supplied layout keeps these rules in its description column. Parse only explicit clauses.
            string description=row.Get("description")??"";
            var xp=Regex.Match(description,@"勝利EXP\s+(\d+)");if(xp.Success)q.experience=int.Parse(xp.Groups[1].Value,CultureInfo.InvariantCulture);
            var gems=Regex.Match(description,@"初回宝石\+(\d+)");if(gems.Success)q.firstClearGems=int.Parse(gems.Groups[1].Value,CultureInfo.InvariantCulture);
            var prerequisite=Regex.Match(description,@"解放条件:\s*([A-Za-z0-9_]+)クリア");
            if(prerequisite.Success){q.prerequisiteId=prerequisite.Groups[1].Value;Require(levels,q.prerequisiteId,row,"description");}
            q.firstClearSpirit=description.Contains("初回スピリター確定1体");
            q.masterRewards=rewardRows.Where(r=>r.Required("quest_lv_id")==q.id).OrderBy(r=>r.Int("number",1)).Select(r=>{
                var reward=new QuestRewardEntry {id=r.Required("id")};
                int kind=r.Int("item_type",0,1);
                if(kind==1)
                {
                    var spirit=Require(spirits,r.Required("sp_id"),r,"sp_id");reward.spiritIndex=Array.IndexOf(data.home.catalog,spirit);
                    reward.probability=Rate(r,"s_reward_rate");reward.min=r.Int("s_reward_min",1);reward.max=r.Int("s_reward_max",reward.min);
                }
                else
                {
                    string id=r.Required("item_id");Require(items,id,r,"item_id");
                    if(!Items.TryGetValue(id,out reward.itemKind))throw r.Error("item_id","このアイテムの報酬処理が未対応です: "+id);
                    reward.probability=Rate(r,"i_reward_rate");reward.min=r.Int("i_reward_min",1);reward.max=r.Int("i_reward_max",reward.min);
                }
                return reward;
            }).ToArray();
            quests.Add(q);
        }
        data.home.quests.quests=quests.OrderBy(q=>q.category).ThenBy(q=>q.order).ThenBy(q=>q.difficulty).ToArray();
    }
    static void ApplyGacha(MasterWorkbook book,MasterGameData data,Dictionary<string,SpiritDefinition> spirits)
    {
        var banners=book.Index("ガチャ");var pool=book.Table("ガチャ中身");var plays=book.Table("ガチャプレイ");
        foreach(var row in pool.Concat(plays))Require(banners,row.Required("gacha_id"),row,"gacha_id");
        data.home.banners=banners.Values.OrderBy(r=>r.Int("number",1)).Select(row=>{
            string id=row.Required("id");var contents=pool.Where(r=>r.Required("gacha_id")==id).OrderBy(r=>r.Int("number",1)).ToArray();
            RequireUniqueOrder(contents);
            var entries=contents.Select(r=>{
                if(r.Int("item_type",0,1)!=1)throw r.Error("item_type","現在のガチャはスピリター抽選です。アイテム抽選は未対応です");
                var spirit=Require(spirits,r.Required("sp_id"),r,"sp_id");
                return new GachaEntry {masterId=r.Required("id"),spiritIndex=Array.IndexOf(data.home.catalog,spirit),quantity=r.Int("s_quantity",1),weight=r.Int("weight",0)};
            }).ToArray();
            var featured=contents.Where(r=>r.Bool("is_pickup")).Select(r=>Array.IndexOf(data.home.catalog,spirits[r.Required("sp_id")])).ToArray();
            if(entries.Length==0)throw row.Error("id","ガチャの中身が空です");
            var options=plays.Where(r=>r.Required("gacha_id")==id).OrderBy(r=>r.Int("number",1)).Select(r=>new GachaPlayOption {
                id=r.Required("id"),order=r.Int("number",1),cost=r.Int("gem_price",0),drawCount=r.Int("item_type",1),paidOnly=r.Bool("is_paid_only"),
                purchaseLimit=OptionalInt(r,"purchase_limit",0),resetPeriod=r.Int("reset_purchase_limit_ID",0,4)
            }).ToArray();
            if(options.Length==0)throw row.Error("id","ガチャプレイの設定が空です");
            return new GachaBanner {masterId=id,title=row.Required("name"),description=row.Get("description"),bannerTexture=Texture(row,"banner_tex_id"),
                availableFrom=book.Date(row,"day_start"),availableUntil=book.Date(row,"day_end"),kind=(GachaKind)Math.Min(3,row.Int("number",1)-1),
                pool=entries,plays=options,featuredIndices=featured,featuredIndex=featured.Length>0?featured[0]:entries[0].spiritIndex,rarityWeights=null};
        }).ToArray();
    }
}
