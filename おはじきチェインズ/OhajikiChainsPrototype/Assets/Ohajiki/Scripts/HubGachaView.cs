using System;
using TMPro;
using UnityEngine;
namespace Ohajiki
{
    public sealed partial class HubView
    {
        public GachaSequence GachaMovie { get; private set; }
        public int FeaturedSpiritId { get; private set; }
        UnityEngine.UI.Image featuredArt;
        TMP_Text featuredName,featuredPosition;
        int featuredPage;
        float featuredTimer,featuredFade;
        int[] FeaturedIds()
        {
            var banner=session.Config.banners[selectedBanner];
            return banner.featuredIndices!=null && banner.featuredIndices.Length>0?banner.featuredIndices:new[]{banner.featuredIndex};
        }
        void BuildBannerDetail()
        {
            var banner=session.Config.banners[selectedBanner];
            Heading(banner.title,banner.kind==GachaKind.Paid?"有償枠：今回は共通の宝石で試作":"召喚の扉をひらこう");
            skin.Halo(content,new Vector2(328,591),385,SpiritUiSkin.Gold);
            var portrait=ui.Rect(content,"Featured portrait",138,375,380,425);
            featuredArt=portrait.gameObject.AddComponent<UnityEngine.UI.Image>();featuredArt.raycastTarget=false;featuredArt.preserveAspect=true;
            featuredName=ui.Label(content,"",40,317,576,65,31,null,true);
            featuredPosition=ui.Label(content,"",170,758,316,40,17,HubWidgets.Muted,true);
            featuredPage=0;ChangeFeatured(0);
            if(FeaturedIds().Length>1)
            {
                ui.Button(content,"＜",12,560,62,74,()=>ChangeFeatured(-1));
                ui.Button(content,"＞",582,560,62,74,()=>ChangeFeatured(1));
            }
            ui.Label(content,banner.description,32,250,592,65,20,HubWidgets.Muted,true);
            bool open=session.BannerOpen(selectedBanner);
            string single=(session.FirstSingleUsed(selectedBanner)?"1連":"初回1連")+$"\n宝石 {session.GachaCost(selectedBanner,1)}個";
            ui.Button(content,single,14,137,300,96,()=>RequestGacha(1),open,HubWidgets.Accent).name="Button_GachaSingle";
            ui.Button(content,$"10連　宝石 {session.GachaCost(selectedBanner,10)}個\nおまけ 宝石{session.Config.tenBonusGems}個",342,137,300,96,()=>RequestGacha(10),open,new Color(.96f,.68f,.18f)).name="Button_GachaTen";
            ui.Button(content,"戻る",14,49,190,66,()=>Navigate(HubPage.Gacha));
            ui.Button(content,"排出率・排出内容",224,49,418,66,ShowRates);
            Message(open?$"所持宝石 {session.Gems}個 / 初回割引は全ガチャ共通1回":"開催期間外です");
        }
        void ChangeFeatured(int delta)
        {
            int[] ids=FeaturedIds();featuredPage=(featuredPage+delta+ids.Length)%ids.Length;
            FeaturedSpiritId=ids[featuredPage];var spirit=session.Config.catalog[FeaturedSpiritId];
            featuredArt.sprite=spirit.portrait;featuredName.text=$"{new string('★',spirit.rarity)}  {spirit.displayName}";
            featuredPosition.text=$"PICK UP  {featuredPage+1} / {ids.Length}";
            featuredTimer=0;featuredFade=0;
        }
        void UpdateFeatured()
        {
            if(Page!=HubPage.Banner || HasModal || featuredArt==null)return;
            featuredTimer+=Time.unscaledDeltaTime;featuredFade=Mathf.Min(1,featuredFade+Time.unscaledDeltaTime*4);
            featuredArt.color=new Color(1,1,1,featuredFade);
            featuredArt.rectTransform.localScale=Vector3.one*Mathf.Lerp(.94f,1,Mathf.SmoothStep(0,1,featuredFade));
            if(featuredTimer>=session.Config.featuredCycleSeconds && FeaturedIds().Length>1)ChangeFeatured(1);
        }
        public void RequestGacha(int count)
        {
            if(GachaMovie!=null || HasModal)return;
            if(!session.CanDraw(selectedBanner,count,out string error))
            {
                Message(error);return;
            }
            int banner=selectedBanner; int quotedCost=session.GachaCost(banner,count);
            bool confirmed=false;
            Confirm($"{count}連ガチャを引きますか？",$"宝石 {quotedCost}個を使います。\n所持 {session.Gems}個"+
                (count==10?$"\nおまけ：宝石{session.Config.tenBonusGems}個":""),()=>
            {
                if(confirmed || GachaMovie!=null)return;
                confirmed=true;
                // Recheck at confirmation: closed banners or a stale quote must not spend.
                if(session.GachaCost(banner,count)!=quotedCost){Render();Message("宝石数が変わりました。もう一度確認してください。");return;}
                if(!session.TryDraw(banner,count,out var receipt,out var failure)){Render();Message(failure);return;}
                OpenModal("SummonMovie",new Color(.04f,.12f,.18f,.94f));
                GachaMovie=modal.gameObject.AddComponent<GachaSequence>();
                GachaMovie.Initialize(ui,skin,session,receipt,()=>
                {
                    GachaMovie=null;CloseModal();Open(HubPage.Gacha);
                });
            });
        }
        void OnDisable()
        {
            // The receipt is already committed; interruption cannot grant or refund twice.
            if(GachaMovie!=null){GachaMovie=null;CloseModal();}
        }
    }
}
