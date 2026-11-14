using System;
using System.IO;
using TMPro;
using UnityEngine;

namespace Ohajiki
{
    // Owns session state and the one active screen / EventSystem.
    public sealed class PrototypeApp : MonoBehaviour
    {
        [SerializeField] BattleTuning battleTuning;
        [SerializeField] HubConfig hubConfig;
        [SerializeField] TMP_FontAsset japaneseFont;
        public PlayerSession Session { get; private set; }
        public HubView Hub { get; private set; }
        public PrototypeBattle Battle { get; private set; }
        public TitleView Title { get; private set; }
        public MasterGameData Master { get; private set; }
        BattleLoadout loadout;
        QuestRun questRun;
        string lastQuestId;
        LocalSaveStore saveStore;
        float nextSave;
        string persistenceMessage,lastSaveError;
        public string SaveFilePath=>saveStore?.FilePath;

        public void Configure(BattleTuning battle,HubConfig home,TMP_FontAsset font)
        { battleTuning=battle;hubConfig=home;japaneseFont=font; }

        void Start()
        {
            Master=Resources.Load<MasterGameData>("OhajikiMasterData");
            if(Master!=null){battleTuning=Master.battle;hubConfig=Master.home;}
            if(battleTuning==null || hubConfig==null || japaneseFont==null)
            { Debug.LogError("PrototypeApp configuration is missing");enabled=false;return; }
            if(!battleTuning.Validate(out string error) || !hubConfig.Validate(out error))
            { Debug.LogError(error);enabled=false;return; }
            Application.targetFrameRate=60;
            Session=new PlayerSession(hubConfig,battleTuning);
            var args=Environment.GetCommandLineArgs();
            bool smoke=Array.Exists(args,arg=>arg.EndsWith("smoke-test",StringComparison.Ordinal));
            bool persistenceSmoke=Array.IndexOf(args,"--persistence-smoke-test")>=0;
            // Existing smoke fixtures must never read or overwrite the user's actual progress.
            if(!smoke || persistenceSmoke)
            {
                string directory=Application.persistentDataPath;
                if(persistenceSmoke)
                {
                    int index=Array.IndexOf(args,"--persistence-directory");
                    if(index<0 || index+1>=args.Length){Debug.LogError("Persistence smoke directory is required");Application.Quit(2);return;}
                    directory=args[index+1];
                }
                saveStore=new LocalSaveStore(directory);
                saveStore.AcquireWriteLock(out string lockError);
                var saved=saveStore.Load(out persistenceMessage);
                if(saved!=null && !Session.RestoreSave(saved,out string restoreError))
                {saveStore.ProtectOriginal();persistenceMessage=restoreError;}
                persistenceMessage=lockError??persistenceMessage;
                if(persistenceMessage!=null)Debug.LogWarning(persistenceMessage);
                Debug.Log("LOCAL SAVE: "+saveStore.FilePath);
            }
            if(smoke && !persistenceSmoke && Array.IndexOf(args,"--title-smoke-test")<0)
                Session.SetPlayerName("検証プレイヤー");
            bool bypassTitle=Array.Exists(args,arg=>arg=="--smoke-test" || arg=="--feedback-smoke-test" ||
                arg=="--hub-smoke-test" || arg=="--shop-smoke-test" || arg=="--gacha-smoke-test" || arg=="--spec-smoke-test" || arg=="--board-smoke-test");
            if(bypassTitle)StartGame();
            else ShowTitle();
            if(Array.IndexOf(args,"--title-smoke-test")>=0)
                gameObject.AddComponent<TitleSmoke>().Run(this);
            if(Array.IndexOf(args,"--master-smoke-test")>=0)
                gameObject.AddComponent<MasterDataSmoke>().Run(this);
            if(Array.IndexOf(args,"--smoke-test")>=0 || Array.IndexOf(args,"--feedback-smoke-test")>=0 || Array.IndexOf(args,"--board-smoke-test")>=0)EnterBattle();
            if(Array.IndexOf(args,"--board-smoke-test")>=0)gameObject.AddComponent<BoardSmoke>().Run(this,battleTuning);
            if(Array.IndexOf(args,"--hub-smoke-test")>=0)
                gameObject.AddComponent<HubSmoke>().Run(this);
            if(Array.IndexOf(args,"--shop-smoke-test")>=0)
                gameObject.AddComponent<ShopSmoke>().Run(this);
            if(Array.IndexOf(args,"--spec-smoke-test")>=0)
                gameObject.AddComponent<SpecificationSmoke>().Run(this);
            if(Array.IndexOf(args,"--gacha-smoke-test")>=0)
                gameObject.AddComponent<GachaSmoke>().Run(this);
            if(persistenceSmoke)gameObject.AddComponent<PersistenceSmoke>().Run(this,args);
        }

        void LateUpdate()
        {
            if(Time.unscaledTime<nextSave)return;
            nextSave=Time.unscaledTime+1;FlushSave();
        }
        public bool FlushSave()
        {
            if(saveStore==null || Session==null)return true;
            if(!saveStore.CanWrite)return false;
            bool success=saveStore.Save(Session.CaptureSave(),out string error);
            if(!success && error!=lastSaveError)
            {
                Debug.LogWarning(error);persistenceMessage=error;
                if(Hub!=null && Hub.gameObject.activeInHierarchy)Hub.Message(error);
            }
            lastSaveError=error;return success;
        }
        void OnApplicationPause(bool paused){if(paused)FlushSave();}
        void OnApplicationFocus(bool focused){if(!focused)FlushSave();}
        void OnApplicationQuit()=>FlushSave();
        void OnDisable()=>FlushSave();

        public void StartGame()
        {
            if(Hub!=null)return;
            if(Title!=null)
            {
                Title.StartRequested-=StartGame;
                Title.gameObject.SetActive(false);Destroy(Title.gameObject);Title=null;
            }
            var canvas=new GameObject("HubCanvas");canvas.transform.SetParent(transform,false);
            Hub=canvas.AddComponent<HubView>();Hub.Initialize(Session,battleTuning,japaneseFont);
            Hub.BattleRequested+=EnterBattle;
            Hub.QuestRequested+=EnterQuest;
            Hub.TitleRequested+=ReturnToTitle;
            Hub.ResetRequested+=ResetAccount;
            if(persistenceMessage!=null)Hub.Message(persistenceMessage);
        }
        void ShowTitle()
        {
            var canvas=new GameObject("TitleCanvas");canvas.transform.SetParent(transform,false);
            Title=canvas.AddComponent<TitleView>();Title.Initialize(hubConfig,japaneseFont);
            Title.StartRequested+=StartGame;
        }
        void ReturnToTitle()
        {
            RemoveBattle();
            if(Hub!=null)
            {
                Hub.BattleRequested-=EnterBattle;Hub.QuestRequested-=EnterQuest;
                Hub.TitleRequested-=ReturnToTitle;Hub.ResetRequested-=ResetAccount;
                Hub.gameObject.SetActive(false);Destroy(Hub.gameObject);Hub=null;
            }
            lastQuestId=null;
            if(Title==null)ShowTitle();
        }
        void ResetAccount()
        {
            var fresh=new PlayerSession(hubConfig,battleTuning);
            if(saveStore!=null && !saveStore.Reset(fresh.CaptureSave(),out string error))
            {Debug.LogWarning("ACCOUNT RESET: "+error);Hub?.ShowResetFailure(error);return;}
            ReturnToTitle();Session=fresh;persistenceMessage=lastSaveError=null;
        }
        public void EnterQuest(string id)
        {
            if(Battle!=null || !Session.HasPlayerName)return;
            if(!Session.TryBeginQuest(id,out var run,out string error)){Hub.Message(error);return;}
            questRun=run;lastQuestId=id;
            try
            {
                loadout=new BattleLoadout(Session,battleTuning,run.Quest);
                Hub.gameObject.SetActive(false);
                var runtime=new GameObject("RuntimeBattle");runtime.transform.SetParent(transform,false);
                Battle=runtime.AddComponent<PrototypeBattle>();
                Battle.ConfigureSession(loadout.Tuning,japaneseFont,Session.Progress);
                Battle.ConfigureQuest(run.Quest,won=>Session.CompleteQuest(run,won,battleTuning));
                Battle.HomeRequested+=ReturnHome;Battle.RetryRequested+=RetryQuest;
            }
            catch {RemoveBattle();Hub.gameObject.SetActive(true);throw;}
        }
        void RetryQuest()
        {
            string id=lastQuestId;
            RemoveBattle();Hub.gameObject.SetActive(true);Hub.OpenQuest(id);
            Hub.RequestQuest(id);
        }

        public void EnterBattle()
        {
            if(Hub==null || !Session.HasPlayerName)return;
            lastQuestId=null;
            if(!Session.TryEnterBattle())
            {
                ReturnHome();Hub.Message("エネルギーが不足しています。自然回復を待つか、＋から回復できます。");return;
            }
            RemoveBattle();
            Hub.gameObject.SetActive(false);
            loadout=new BattleLoadout(Session,battleTuning);
            var runtime=new GameObject("RuntimeBattle");runtime.transform.SetParent(transform,false);
            Battle=runtime.AddComponent<PrototypeBattle>();
            Battle.ConfigureSession(loadout.Tuning,japaneseFont,Session.Progress);
            Battle.HomeRequested+=ReturnHome;Battle.RetryRequested+=EnterBattle;
            Battle.ExperienceEarned+=AwardSpiritExperience;
        }
        void AwardSpiritExperience(int experience)
        {
            if(loadout != null) Session.AwardBattleExperience(loadout.SpiritIds,experience);
        }
        public void ReturnHome()
        {
            string id=lastQuestId;
            RemoveBattle();Hub.gameObject.SetActive(true);
            if(id!=null)Hub.OpenQuestCategory(Session.Config.quests.Find(id).category);
            else Hub.Navigate(HubPage.Home);
        }
        void RemoveBattle()
        {
            if(Battle!=null)
            {
                Battle.HomeRequested-=ReturnHome;Battle.RetryRequested-=EnterBattle;
                Battle.RetryRequested-=RetryQuest;
                Battle.ExperienceEarned-=AwardSpiritExperience;
                Battle.gameObject.SetActive(false);Destroy(Battle.gameObject);Battle=null;
            }
            loadout?.Dispose();loadout=null;
            Session?.AbandonQuest(questRun);questRun=null;
        }
        void OnDestroy()
        {
            FlushSave();
            saveStore?.Dispose();
            if(Title!=null)Title.StartRequested-=StartGame;
            if(Hub!=null)Hub.BattleRequested-=EnterBattle;
            if(Hub!=null)Hub.QuestRequested-=EnterQuest;
            RemoveBattle();
        }
    }
}
