using UnityEngine;
namespace Ohajiki
{
    public sealed class SpiritBallVisual : MonoBehaviour
    {
        UnityEngine.UI.Image face,body;
        static readonly Color[] Colors={new Color(1,.4f,.13f),new Color(.13f,.65f,1),new Color(.32f,.83f,.19f)};
        public void Initialize(SpiritUiSkin skin)
        {
            body=GetComponent<UnityEngine.UI.Image>();
            if(body==null)body=gameObject.AddComponent<UnityEngine.UI.Image>();
            body.sprite=skin.Ball;body.raycastTarget=false;
            var mask=skin.Image(transform,"Spirit portrait mask",skin.Ball,0,0,1,1,Color.white);
            Stretch(mask.rectTransform,.08f,.92f);mask.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=false;
            face=skin.Image(mask.transform,"Spirit within",null,0,0,1,1,new Color(1,1,1,.58f));
            Stretch(face.rectTransform,0,1);face.preserveAspect=true;
            var glass=skin.Image(transform,"Soft glass",skin.Ball,0,0,1,1,new Color(1,1,1,.12f));Stretch(glass.rectTransform,0,1);
            var shine=skin.Image(transform,"Soft reflection",skin.Star,0,0,1,1,new Color(1,1,1,.45f));
            shine.rectTransform.anchorMin=new Vector2(.18f,.69f);shine.rectTransform.anchorMax=new Vector2(.34f,.86f);
            shine.rectTransform.offsetMin=shine.rectTransform.offsetMax=Vector2.zero;
        }
        static void Stretch(RectTransform rect,float from,float to)
        {rect.anchorMin=Vector2.one*from;rect.anchorMax=Vector2.one*to;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        public void Show(SpiritDefinition spirit)
        {
            SetPortrait(spirit.portrait);
            body.color=Colors[(int)spirit.element];
        }
        public void SetPortrait(Sprite sprite)
        { face.sprite=sprite;face.enabled=sprite!=null; }
    }
}
