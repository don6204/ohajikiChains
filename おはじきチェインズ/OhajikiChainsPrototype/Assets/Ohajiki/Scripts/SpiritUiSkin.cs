using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ohajiki
{
    // Per-canvas presentation assets. Runtime textures never outlive their owning screen.
    public sealed class SpiritUiSkin : MonoBehaviour
    {
        public static readonly Color Gold=new Color(1,.72f,.15f);
        public static readonly Color Ink=new Color(.24f,.16f,.08f);
        public static readonly Color Muted=new Color(.43f,.33f,.22f);
        public static readonly Color Panel=new Color(1,.96f,.80f,.97f);
        public static readonly Color Accent=new Color(.08f,.53f,.58f);
        public static readonly Color Edge=new Color(.72f,.42f,.13f);
        public static readonly Color Paper=new Color(1,.98f,.90f);
        public Sprite Plate,Border,Ball,Ring,Glow,Star,Shard,Burst,Fade,DamageBurst;
        Material outlineMaterial;
        readonly List<Sprite> sprites=new List<Sprite>();
        readonly List<Texture2D> textures=new List<Texture2D>();
        public void Initialize()
        {
            if(Plate!=null)return;
            outlineMaterial=Resources.Load<Material>("UI/FeedbackOutline");
            Plate=Make(0,64);Border=Make(1,64);Ball=Make(2,128);Ring=Make(3,128);
            Glow=Make(4,128);Star=Make(5,64);Shard=Make(6,64);Burst=Make(7,256);Fade=Make(8,32);
            DamageBurst=Make(9,256);
        }
        Sprite Make(int kind,int size)
        {
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Spirit UI "+kind,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float px=(x+.5f)/size*2-1,py=(y+.5f)/size*2-1,r=Mathf.Sqrt(px*px+py*py),a=1,v=1;
                Color color=Color.white;
                if(kind<2)
                {
                    var q=new Vector2(Mathf.Abs(px)-.73f,Mathf.Abs(py)-.73f);
                    float d=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-.24f;
                    a=Mathf.Clamp01(-d*size*.5f);
                    if(kind==1)a*=1-Mathf.Clamp01((-d-.055f)*size*.5f);
                    v=kind==0?.84f+.16f*(py+1)*.5f:1;
                }
                if(kind==2)
                {
                    a=Mathf.Clamp01((.97f-r)*size*.5f);
                    float z=Mathf.Sqrt(Mathf.Max(0,1-r*r));
                    v=.38f+.55f*Mathf.Max(0,Vector3.Dot(new Vector3(px,py,z),new Vector3(-.35f,.5f,.8f).normalized));
                    v+=.5f*Mathf.Exp(-((px+.30f)*(px+.30f)+(py-.38f)*(py-.38f))*60);
                    v+=.3f*Mathf.Exp(-Mathf.Pow((r-.85f)*24,2));
                }
                if(kind==3)a=Mathf.Clamp01((.035f-Mathf.Abs(r-.88f))*size*.5f);
                if(kind==4)a=Mathf.Pow(Mathf.Max(0,1-r),2.2f);
                if(kind==5)a=Mathf.Clamp01((.15f-Mathf.Abs(px*py)-.12f*(px*px+py*py))*size);
                if(kind==6)a=Mathf.Clamp01((1-Mathf.Abs(px)-Mathf.Abs(py))*size*.5f);
                if(kind==7 || kind==9)
                {
                    float angle=Mathf.Atan2(py,px),sector=Mathf.Repeat(angle/(2*Mathf.PI)*14,1);
                    float limit=Mathf.Lerp(.62f,.98f,Mathf.Abs(sector*2-1))*(.96f+.04f*Mathf.Sin(angle*5));
                    a=Mathf.Clamp01((limit-r)*size*.5f);
                    color=kind==7?Color.Lerp(new Color(1,.49f,.08f),new Color(1,.95f,.46f),Mathf.Clamp01((py+1)*.65f)):
                        Color.Lerp(new Color(.87f,.87f,.87f),Color.white,Mathf.Clamp01((py+1)*.65f));
                }
                if(kind==8)a=(py+1)*.5f;
                pixels[y*size+x]=new Color(color.r*v,color.g*v,color.b*v,a);
            }
            texture.SetPixels(pixels);texture.Apply(false,true);textures.Add(texture);
            var sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,
                kind<2?new Vector4(18,18,18,18):Vector4.zero);
            sprites.Add(sprite);return sprite;
        }
        public static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=rt.pivot=Vector2.zero;rt.anchoredPosition=new Vector2(x,y);rt.sizeDelta=new Vector2(w,h);return rt;
        }
        public UnityEngine.UI.Image Image(Transform parent,string name,Sprite sprite,float x,float y,float w,float h,Color color,bool centered=false)
        {
            var rt=Rect(parent,name,x,y,w,h);if(centered)rt.pivot=new Vector2(.5f,.5f);
            var image=rt.gameObject.AddComponent<UnityEngine.UI.Image>();image.sprite=sprite;image.color=color;image.raycastTarget=false;return image;
        }
        public void Frame(UnityEngine.UI.Image surface,Color? edge=null)
        {
            surface.sprite=Plate;surface.type=UnityEngine.UI.Image.Type.Sliced;
            var border=Image(surface.transform,"Gilded edge",Border,0,0,1,1,edge??Edge);
            border.type=UnityEngine.UI.Image.Type.Sliced;
            border.rectTransform.anchorMin=Vector2.zero;border.rectTransform.anchorMax=Vector2.one;
            border.rectTransform.offsetMin=border.rectTransform.offsetMax=Vector2.zero;
            var shadow=surface.gameObject.AddComponent<UnityEngine.UI.Shadow>();shadow.effectColor=new Color(.31f,.17f,.04f,.4f);shadow.effectDistance=new Vector2(0,-5);
        }
        public void Backdrop(RectTransform parent,bool battle)
        {
            var art=Resources.Load<Sprite>("UI/SunnyRuins");
            if(art!=null)Image(parent,"Sunny ruins",art,0,0,720,1280,Color.white);
            Image(parent,"Sky mist",Fade,0,0,720,1280,new Color(1,1,.95f,battle?.22f:.12f));
            Image(parent,"Footer mist",Fade,720,290,720,290,new Color(1,.97f,.85f,.88f)).rectTransform.localRotation=Quaternion.Euler(0,0,180);
            Image(parent,"Top trim",null,28,1271,664,3,Gold);
        }
        public void Halo(Transform parent,Vector2 center,float diameter,Color color)
        {
            Image(parent,"Spirit aura",Glow,center.x,center.y,diameter*1.3f,diameter*1.3f,new Color(color.r,color.g,color.b,.55f),true);
            Image(parent,"Summoning circle",Ring,center.x,center.y,diameter,diameter,new Color(color.r,color.g,color.b,.5f),true);
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI/4;Vector2 p=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*diameter*.45f;
                Image(parent,"Rune",Shard,p.x,p.y,9,16,color,true).rectTransform.localRotation=Quaternion.Euler(0,0,-i*45);
            }
        }
        public void Text(TMP_Text text,bool emphasis=false)
        {
            if(emphasis)
            {
                text.fontStyle|=FontStyles.Bold;
                if(outlineMaterial!=null)text.fontSharedMaterial=outlineMaterial;
                text.fontMaterial.EnableKeyword("OUTLINE_ON");
                text.outlineWidth=.18f;text.outlineColor=new Color(.26f,.12f,.025f,1);
                text.extraPadding=true;
            }
        }
        public void NavigationIcon(Transform parent,int kind,Color color)
        {
            // Small, original symbols, accompanied by text labels.
            if(kind==0)
            {
                Image(parent,"Home roof",Shard,62,55,31,30,color,true);
                Image(parent,"Home base",null,51,39,22,16,color);
                Image(parent,"Door",null,59,39,7,11,Panel);
            }
            else if(kind==1)
            {
                Image(parent,"Spirit cards",Border,45,41,23,29,color).type=UnityEngine.UI.Image.Type.Sliced;
                Image(parent,"Spirit card",Border,56,45,23,29,color).type=UnityEngine.UI.Image.Type.Sliced;
            }
            else if(kind==2)Image(parent,"Summon crystal",Shard,62,55,25,36,color,true);
            else if(kind==3)
            {
                Image(parent,"Shop bag",Plate,46,39,32,27,color).type=UnityEngine.UI.Image.Type.Sliced;
                Image(parent,"Bag handle",Ring,62,64,20,20,color,true);
                Image(parent,"Bag star",Star,62,51,15,15,Panel,true);
            }
            else
            {
                Image(parent,"Settings",Ring,62,55,30,30,color,true);
                for(int i=0;i<6;i++)Image(parent,"Cog",null,62,55,6,35,color,true).rectTransform.localRotation=Quaternion.Euler(0,0,i*60);
                Image(parent,"Cog core",Ball,62,55,19,19,Panel,true);
            }
        }
        void OnDestroy(){foreach(var sprite in sprites)if(sprite!=null)Destroy(sprite);foreach(var texture in textures)if(texture!=null)Destroy(texture);}
    }
}
