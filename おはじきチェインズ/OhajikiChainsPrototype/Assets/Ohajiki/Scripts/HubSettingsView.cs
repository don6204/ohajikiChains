using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ohajiki
{
    public sealed partial class HubView
    {
        int favoritePage;
        static readonly Color SettingsBackground=new Color(.96f,.97f,.91f);
        void SettingsPage()
        {
            Heading("設定","おはじきチェインズ  Ver. "+Application.version);
            var list=ui.Scroll(content,12,24,632,785);
            SettingButton(list,"プロフィール",ShowProfile);
            SettingButton(list,"オプション",ShowOptions);
            SettingButton(list,"戦友",()=>ShowInformation("戦友",$"戦友枠：0 / {session.FriendCapacity}\n戦友機能は準備中です。"));
            SettingButton(list,"お知らせ",ShowNews);
            SettingButton(list,"図鑑",ShowCatalog);
            SettingButton(list,"データ引継ぎ",null);
            SettingButton(list,"お問い合わせ",null);
            SettingButton(list,"ご意見・ご要望",null);
            SettingButton(list,"公式サイト",null);
            SettingButton(list,"公式X",()=>Application.OpenURL(TitleView.SocialUrl));
            SettingButton(list,"利用規約",null);
            SettingButton(list,"ヘルプ",ShowHelp);
            SettingButton(list,"プライバシーポリシー",null);
            SettingButton(list,"特定商取引法に基づく表記",()=>ShowLegalNotice(null));
            SettingButton(list,"コピーライト",null);
            SettingButton(list,"アカウント初期化",()=>Confirm("アカウント初期化","この端末に保存した進行状況と設定を初期化し、\nタイトルへ戻ります。",()=>ResetRequested?.Invoke()));
            SettingButton(list,"タイトルへ",()=>TitleRequested?.Invoke());
        }
        void SettingButton(RectTransform list,string label,Action action)
        {
            var row=ui.Row(list,64);
            ui.Button(row,label+(action==null?"（準備中）":""),0,0,614,64,action,action!=null);
        }
        void SettingsModal(string title,string name)
        {
            OpenModal(name,SettingsBackground);
            ui.Label(modal,title,48,1120,624,76,34,null,true);
        }
        void ModalBack(Action back=null)
        {ui.Button(modal,"戻る",160,64,400,72,()=>{CloseModal();back?.Invoke();});}
        void ShowInformation(string title,string message)
        {
            SettingsModal(title,"Information");
            ui.Label(modal,message,64,340,592,620,25,null,true);
            ModalBack();
        }
        public void ShowResetFailure(string message)
        {
            SettingsModal("初期化できませんでした","ResetFailure");
            ui.Label(modal,message,64,340,592,620,25,null,true);
            ui.Button(modal,"初期化を再試行",120,180,480,72,()=>{CloseModal();ResetRequested?.Invoke();});
            ModalBack();
        }
        void ShowNews()=>ShowInformation("お知らせ","新しいお知らせはありません。");
        void ShowHelp()=>ShowInformation("遊び方",
            "手札を選び、発射位置を調整して上へスワイプ。\n同属性のチャージボールでスキルLv上昇。\n手札長押しでスキルを確認できます。\n\n"+
            "接触ごとに通常攻撃の威力を加算し、\nダメージUIの着弾時にHPへまとめて反映します。\n\n"+
            "エネルギーは5分で1回復。\nヘッダーの＋から宝石で全回復できます。\n\n進行状況はこの端末に自動保存されます。\n戦闘の途中からの再開には対応していません。");
        void CycleFavorite(int delta)
        {
            for(int i=0;i<3;i++)
            {favoritePage=(favoritePage+delta+3)%3;if(session.Favorite(favoritePage)>=0)break;}
            Render();
        }
        void PlainLabel(Transform parent,string value,float x,float y,float w,float h,int size=24)
        {
            var label=ui.Label(parent,value,x,y,w,h,size);
            label.richText=false;label.overflowMode=TextOverflowModes.Ellipsis;
        }
        void ShowProfile()
        {
            SettingsModal("プロフィール","Profile");
            for(int slot=0;slot<3;slot++)
            {
                int selected=slot,id=session.Favorite(slot);float x=48+slot*208;
                var card=ui.Button(modal,"",x,866,200,220,()=>ChooseFavorite(selected)).transform;
                card.name="Button_Favorite_"+slot;
                ui.Label(card,slot==0?"① プレイヤーアイコン":"お気に入り "+(slot+1),6,174,188,40,15,null,true);
                if(id>=0)
                {
                    var spirit=session.Config.catalog[id];ui.Portrait(card,spirit.portrait,44,54,112,122);
                    var label=ui.Label(card,spirit.displayName,8,8,184,48,20,null,true);
                    label.enableAutoSizing=true;label.fontSizeMin=14;label.fontSizeMax=20;
                }
                else ui.Label(card,"未設定",8,60,184,90,24,HubWidgets.Muted,true);
            }
            ui.Label(modal,"二つ名："+(session.PlayerTitle??"未設定"),48,802,624,48,22,HubWidgets.Muted);
            PlainLabel(modal,session.PlayerName??"名前未設定",48,723,460,64,28);
            ui.Button(modal,"名前変更",520,726,152,60,()=>EditProfileText(true,false));
            PlainLabel(modal,session.Greeting,48,627,460,80);
            ui.Button(modal,"ひとこと変更",520,637,152,60,()=>EditProfileText(false,false));
            ui.Label(modal,"プレイヤーID："+(session.PlayerId??"未発行"),48,552,460,56,23);
            ui.Button(modal,"コピー",520,552,152,56,()=>GUIUtility.systemCopyBuffer=session.PlayerId,session.PlayerId!=null);
            var stats=ui.Panel(modal,"ProfileStatistics",48,226,624,300,HubWidgets.PanelColor);
            ui.Label(stats,$"ゲーム開始日　{session.StartedOn:yyyy/MM/dd}\nログイン総日数　{session.LoginDays}日\nキャラクター取得数　{session.AcquiredSpiritCount:N0}\nクエスト挑戦回数　{session.QuestAttempts:N0}\nマルチプレイ回数　{session.MultiplayerCount:N0}",24,16,576,268,24);
            ui.Label(modal,"記録・設定はこの試作版の起動中に保持されます。",48,168,624,44,18,HubWidgets.Muted,true);
            ModalBack(()=>{if(Page==HubPage.Home)Render();});
        }
        void ChooseFavorite(int slot)
        {
            SettingsModal("お気に入り "+(slot+1)+" を選ぶ","FavoriteSelection");
            var list=ui.Scroll(modal,48,260,624,800);
            foreach(int id in session.FindSpirits(-1,0,0))
            {
                int selected=id;var spirit=session.Config.catalog[id];var row=ui.Row(list,92);
                var button=ui.Button(row,"",0,0,606,92,()=>{session.SetFavorite(slot,selected);ShowProfile();}).transform;
                button.name="Button_FavoriteSpirit_"+id;
                ui.Portrait(button,spirit.portrait,12,8,66,76);
                ui.Label(button,spirit.displayName+"　Lv"+session.Level(id),96,0,496,92,24);
            }
            if(slot>0)ui.Button(modal,"この枠を未設定にする",100,168,520,64,()=>{session.SetFavorite(slot,-1);ShowProfile();});
            ModalBack(ShowProfile);
        }
        void EditProfileText(bool name,bool required)
        {
            SettingsModal(name?"プレイヤー名を入力":"ひとことを入力",required?"RequiredPlayerName":"ProfileText");
            ui.Label(modal,required?"冒険を始める前に、名前を決めましょう。":"内容を入力して保存してください。",48,930,624,90,25,null,true);
            var fieldRoot=ui.Panel(modal,"ProfileInput",64,768,592,86,Color.white,true);
            var viewport=ui.Rect(fieldRoot,"TextViewport",16,10,560,66);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text=ui.Label(viewport,"",0,0,560,66,28);text.richText=false;
            text.textWrappingMode=TextWrappingModes.NoWrap;
            var input=fieldRoot.gameObject.AddComponent<TMP_InputField>();
            input.textViewport=viewport;input.textComponent=text;
            input.targetGraphic=fieldRoot.GetComponent<Image>();input.lineType=TMP_InputField.LineType.SingleLine;
            input.text=name?(session.PlayerName??""):session.Greeting;
            var error=ui.Label(modal,"",64,662,592,80,22,new Color(.7f,.14f,.1f),true);
            ui.Button(modal,required?"冒険をはじめる":"保存",100,530,520,80,()=>{
                if(name && !session.SetPlayerName(input.text)){error.text="名前を入力してください。";return;}
                if(!name)session.SetGreeting(input.text);
                CloseModal();if(required)Render();else ShowProfile();
            },true,HubWidgets.Accent);
            if(!required)ModalBack(ShowProfile);
        }
        void ShowOptions()
        {
            SettingsModal("オプション","Options");
            ui.Label(modal,"サウンド設定",48,1030,624,62,28);
            string[] channels={"BGM","SE","ボイス"};
            for(int i=0;i<3;i++)
            {
                int channel=i;float y=922-i*126;
                var label=ui.Label(modal,channels[i]+"　"+session.Volume(i),64,y+38,592,48,24);
                var root=ui.Rect(modal,"Volume_"+channels[i],80,y,560,32);
                ui.Panel(root,"Track",0,10,560,12,new Color(.73f,.78f,.73f),true);
                var fillArea=ui.Rect(root,"FillArea",0,10,560,12);
                var fill=ui.Panel(fillArea,"Fill",0,0,560,12,HubWidgets.Accent);
                var handleArea=ui.Rect(root,"HandleArea",12,0,536,32);
                var handle=ui.Panel(handleArea,"Handle",0,0,28,32,SpiritUiSkin.Gold,true);
                var slider=root.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=100;slider.wholeNumbers=true;
                // Slider drives stretch anchors: fixed size offsets would extend the bar beyond its track.
                fill.sizeDelta=Vector2.zero;fill.anchoredPosition=Vector2.zero;
                handle.sizeDelta=new Vector2(28,0);
                slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();slider.value=session.Volume(i);
                slider.onValueChanged.AddListener(value=>{session.SetVolume(channel,(int)value);label.text=channels[channel]+"　"+(int)value;});
            }
            ui.Label(modal,"通知設定",48,544,624,62,28);
            string[] notices={"対戦申込","運営からの通知","エネルギー満タン"};
            for(int i=0;i<notices.Length;i++)
            {
                int category=i;float y=456-i*92;bool enabled=session.Notification(i);
                ui.Label(modal,notices[i],64,y,324,64,24);
                ui.Button(modal,"ON",412,y,112,64,()=>{session.SetNotification(category,true);ShowOptions();},true,enabled?HubWidgets.Accent:HubWidgets.PanelColor);
                ui.Button(modal,"OFF",540,y,112,64,()=>{session.SetNotification(category,false);ShowOptions();},true,!enabled?HubWidgets.Accent:HubWidgets.PanelColor);
            }
            ui.Label(modal,"試作版では設定のみ保存。音声・通知配信は準備中です。",48,170,624,66,18,HubWidgets.Muted,true);
            ModalBack();
        }
        void ShowCatalog()
        {
            SettingsModal("図鑑","Catalog");var list=ui.Scroll(modal,48,176,624,900);
            for(int i=0;i<session.Config.catalog.Length;i++)
            {
                var spirit=session.Config.catalog[i];var row=ui.Row(list,132);
                ui.Portrait(row,spirit.portrait,8,12,94,108);
                ui.Label(row,spirit.displayName,120,68,470,56,25);
                ui.Label(row,new string('★',spirit.rarity)+"　"+(session.Copies(i)>0?"取得済み":"未取得"),120,16,470,48,22,HubWidgets.Muted);
            }
            ModalBack();
        }
        void ShowLegalNotice(Action back)
        {
            SettingsModal("特定商取引法に基づく表記","LegalNotice");
            var list=ui.Scroll(modal,64,260,592,800);var row=ui.Row(list,780);
            ui.Label(row,session.Config.shop?.legalNotice??"販売条件は未設定です。",12,0,560,780,23);
            ModalBack(back);
        }
    }
}
