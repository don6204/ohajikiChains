using System;
using TMPro;
using UnityEngine;

namespace Ohajiki
{
    public sealed class HubWidgets
    {
        readonly TMP_FontAsset font;
        readonly SpiritUiSkin skin;
        public static readonly Color Ink = SpiritUiSkin.Ink;
        public static readonly Color Muted = SpiritUiSkin.Muted;
        public static readonly Color PanelColor = SpiritUiSkin.Panel;
        public static readonly Color Accent = SpiritUiSkin.Accent;
        public static readonly Color[] Elements = {new Color(.85f,.23f,.08f), new Color(.02f,.43f,.68f), new Color(.13f,.48f,.18f)};
        public HubWidgets(TMP_FontAsset font,SpiritUiSkin skin=null) { this.font=font;this.skin=skin; }
        public RectTransform Rect(Transform parent,string name,float x,float y,float width,float height)
        {
            var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=rt.pivot=Vector2.zero;
            rt.anchoredPosition=new Vector2(x,y); rt.sizeDelta=new Vector2(width,height); return rt;
        }
        public RectTransform Panel(Transform parent,string name,float x,float y,float width,float height,Color color,bool raycast=false)
        {
            var rt=Rect(parent,name,x,y,width,height);
            var image=rt.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color=color; image.raycastTarget=raycast;
            if(skin!=null && width>30 && height>20 && width<710 && name!="Viewport")skin.Frame(image);
            return rt;
        }
        public TMP_Text Label(Transform parent,string text,float x,float y,float w,float h,int size=24,Color? color=null,bool center=false)
        {
            var label=Rect(parent,"Text",x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font=font; label.text=text; label.fontSize=size; label.color=color??Ink;
            label.raycastTarget=false; label.textWrappingMode=TextWrappingModes.Normal;
            label.alignment=center?TextAlignmentOptions.Center:TextAlignmentOptions.MidlineLeft;
            if(size>=26)label.fontStyle=FontStyles.Bold;
            return label;
        }
        public UnityEngine.UI.Button Button(Transform parent,string title,float x,float y,float w,float h,Action action,bool enabled=true,Color? color=null)
        {
            var rt=Panel(parent,"Button_"+title,x,y,w,h,color??PanelColor,true);
            rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition+=new Vector2(w*.5f,h*.5f);
            var button=rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic=rt.GetComponent<UnityEngine.UI.Image>(); button.interactable=enabled;
            button.onClick.AddListener(()=>action?.Invoke());
            Color fill=color??PanelColor;
            Color textColor=fill.grayscale<.57f?Color.white:Ink;
            Label(rt,title,6,0,w-12,h,20,enabled?textColor:Muted,true);
            if(skin!=null)rt.gameObject.AddComponent<UiButtonMotion>();
            return button;
        }
        public void Portrait(Transform parent,Sprite sprite,float x,float y,float w,float h)
        {
            var image=Rect(parent,"Portrait",x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite=sprite; image.enabled=sprite!=null; image.preserveAspect=true; image.raycastTarget=false;
        }
        public RectTransform Scroll(Transform parent,float x,float y,float w,float h)
        {
            var root=Rect(parent,"Scroll",x,y,w,h);
            var scroll=root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var viewport=Panel(root,"Viewport",0,0,w,h,Color.white,true);
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=false;
            var content=Rect(viewport,"Content",0,0,w,0);
            content.anchorMin=new Vector2(0,1); content.anchorMax=new Vector2(1,1); content.pivot=new Vector2(.5f,1);
            content.sizeDelta=Vector2.zero;
            var layout=content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childControlWidth=true; layout.childControlHeight=true; layout.childForceExpandHeight=false;
            layout.childForceExpandWidth=true; layout.spacing=12; layout.padding=new RectOffset(4,4,4,4);
            var fit=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>(); fit.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport=viewport; scroll.content=content; scroll.horizontal=false; scroll.vertical=true;
            scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=32;
            return content;
        }
        public RectTransform Row(Transform content,float height)
        {
            var row=Rect(content,"Row",0,0,640,height);
            var layout=row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); layout.preferredHeight=height;
            return row;
        }
    }
}
