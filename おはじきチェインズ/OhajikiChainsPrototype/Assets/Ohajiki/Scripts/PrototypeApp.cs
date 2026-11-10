using System;
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
        BattleLoadout loadout;

        public void Configure(BattleTuning battle,HubConfig home,TMP_FontAsset font)
        { battleTuning=battle;hubConfig=home;japaneseFont=font; }

        void Start()
        {
            if(battleTuning==null || hubConfig==null || japaneseFont==null)
            { Debug.LogError("PrototypeApp configuration is missing");enabled=false;return; }
            if(!battleTuning.Validate(out string error) || !hubConfig.Validate(out error))
            { Debug.LogError(error);enabled=false;return; }
            Application.targetFrameRate=60;
            Session=new PlayerSession(hubConfig,battleTuning);
            var args=Environment.GetCommandLineArgs();
            bool bypassTitle=Array.Exists(args,arg=>arg=="--smoke-test" || arg=="--feedback-smoke-test" ||
                arg=="--hub-smoke-test" || arg=="--shop-smoke-test" || arg=="--gacha-smoke-test");
            if(bypassTitle)StartGame();
            else
            {
                var canvas=new GameObject("TitleCanvas");canvas.transform.SetParent(transform,false);
                Title=canvas.AddComponent<TitleView>();Title.Initialize(hubConfig,japaneseFont);
                Title.StartRequested+=StartGame;
            }
            if(Array.IndexOf(args,"--title-smoke-test")>=0)
                gameObject.AddComponent<TitleSmoke>().Run(this);
            if(Array.IndexOf(args,"--smoke-test")>=0 || Array.IndexOf(args,"--feedback-smoke-test")>=0)EnterBattle();
            if(Array.IndexOf(args,"--hub-smoke-test")>=0)
                gameObject.AddComponent<HubSmoke>().Run(this);
            if(Array.IndexOf(args,"--shop-smoke-test")>=0)
                gameObject.AddComponent<ShopSmoke>().Run(this);
            if(Array.IndexOf(args,"--gacha-smoke-test")>=0)
                gameObject.AddComponent<GachaSmoke>().Run(this);
        }

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
        }

        public void EnterBattle()
        {
            if(Hub==null)return;
            if(!Session.TryEnterBattle())
            {
                ReturnHome();Hub.Message("エネルギーが不足しています。試作版では再起動で回復します。");return;
            }
            RemoveBattle();
            Hub.gameObject.SetActive(false);
            loadout=new BattleLoadout(Session,battleTuning);
            var runtime=new GameObject("RuntimeBattle");runtime.transform.SetParent(transform,false);
            Battle=runtime.AddComponent<PrototypeBattle>();
            Battle.ConfigureSession(loadout.Tuning,japaneseFont,Session.Progress);
            Battle.HomeRequested+=ReturnHome;Battle.RetryRequested+=EnterBattle;
        }
        public void ReturnHome()
        {
            RemoveBattle();Hub.gameObject.SetActive(true);Hub.Navigate(HubPage.Home);
        }
        void RemoveBattle()
        {
            if(Battle!=null)
            {
                Battle.HomeRequested-=ReturnHome;Battle.RetryRequested-=EnterBattle;
                Battle.gameObject.SetActive(false);Destroy(Battle.gameObject);Battle=null;
            }
            loadout?.Dispose();loadout=null;
        }
        void OnDestroy()
        {
            if(Title!=null)Title.StartRequested-=StartGame;
            if(Hub!=null)Hub.BattleRequested-=EnterBattle;
            RemoveBattle();
        }
    }
}
