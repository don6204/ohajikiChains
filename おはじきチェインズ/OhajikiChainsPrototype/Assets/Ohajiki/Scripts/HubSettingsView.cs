using System;
using UnityEngine;

namespace Ohajiki
{
    public sealed partial class HubView
    {
        void SettingsPage()
        {
            Heading("設定・遊び方","おはじきチェインズ  Ver. "+Application.version);
            var list=ui.Scroll(content,12,260,632,535);var row=ui.Row(list,520);
            ui.Label(row,"遊び方\n手札を選び、発射位置を調整して上へスワイプ。\n全接触数で通常攻撃、同属性でスキルLv上昇。\n手札長押しでスキルを確認できます。\n\n特殊チャージ\n攻撃アップ・速度アップ・分裂・縦/横ミサイル・ワープ\n\nエネルギー\nヘッダーの＋から宝石1個で全回復。\nランクアップごとに最大値と戦友枠が増加。\n\n試作版\n進行状況は終了時にリセット。購入は模擬処理。\nアカウント連携・戦友・マルチプレイは未接続。",
                12,0,596,520,22,HubWidgets.Ink);
            ui.Label(content,$"戦友枠：{session.FriendCapacity}　/　自然回復は未実装",12,200,632,50,20,HubWidgets.Muted,true);
            ui.Button(content,"特定商取引法に基づく表記",0,115,656,66,()=>ShowLegalNotice(null));
            ui.Button(content,"ホームへ",0,22,656,66,()=>Navigate(HubPage.Home));
        }
        void ShowLegalNotice(Action back)
        {
            OpenModal("LegalNotice",Color.black);
            ui.Label(modal,"特定商取引法に基づく表記",48,1050,624,90,30,null,true);
            var list=ui.Scroll(modal,64,300,592,700);var row=ui.Row(list,680);
            ui.Label(row,session.Config.shop?.legalNotice??"販売条件は未設定です。",12,0,560,680,23);
            ui.Button(modal,"戻る",160,160,400,76,()=>{CloseModal();back?.Invoke();});
        }
    }
}
