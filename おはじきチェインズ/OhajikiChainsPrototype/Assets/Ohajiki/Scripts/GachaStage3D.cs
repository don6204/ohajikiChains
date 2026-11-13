using System.Collections.Generic;
using UnityEngine;
namespace Ohajiki
{
    // An isolated real 3D set, filmed into the existing uGUI screen. No global camera/settings changes.
    public sealed class GachaStage3D : MonoBehaviour
    {
        readonly List<Material> materials=new List<Material>();
        readonly List<Mesh> meshes=new List<Mesh>();
        readonly List<Transform> stars=new List<Transform>();
        GameObject world;
        Camera movieCamera;
        RenderTexture target;
        Transform orb,portrait,innerRing,outerRing,beam;
        Material orbMaterial,portraitMaterial;
        Mesh crystal;
        UnityEngine.UI.RawImage screen;
        static readonly Vector3 Origin=new Vector3(10000,0,0);
        public Camera MovieCamera=>movieCamera;
        public RenderTexture Target=>target;
        public void Initialize(RectTransform parent,HubWidgets ui)
        {
            screen=ui.Rect(parent,"3D summon film",0,0,720,1280).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            screen.raycastTarget=false;
            target=new RenderTexture(540,960,24,RenderTextureFormat.ARGB32){name="Summon film",antiAliasing=1};target.Create();screen.texture=target;
            world=new GameObject("Summon 3D set");world.transform.position=Origin;
            movieCamera=new GameObject("Summon movie camera",typeof(Camera)).GetComponent<Camera>();movieCamera.transform.SetParent(world.transform,false);
            movieCamera.targetTexture=target;movieCamera.cullingMask=1<<31;movieCamera.clearFlags=CameraClearFlags.SolidColor;
            movieCamera.backgroundColor=new Color(.33f,.76f,.93f);movieCamera.nearClipPlane=.1f;movieCamera.farClipPlane=80;movieCamera.fieldOfView=MasterConstants.GetFloat("GachaStage3D.cameraFieldOfView",44);
            movieCamera.allowHDR=false;movieCamera.allowMSAA=false;
            Material stone=Material(new Color(.88f,.70f,.39f)),lightStone=Material(new Color(1,.9f,.65f)),gold=Material(new Color(1,.7f,.11f),.14f),teal=Material(new Color(.05f,.7f,.75f),.3f),grass=Material(new Color(.25f,.65f,.26f));
            Material floor=Material(new Color(1,.98f,.87f));floor.mainTexture=Resources.Load<Sprite>("UI/SandstoneBoard").texture;
            Piece(PrimitiveType.Cylinder,"Floating sanctuary",new Vector3(0,-.45f,0),new Vector3(16,.45f,16),stone);
            Piece(PrimitiveType.Cylinder,"Garden terrace",new Vector3(0,.02f,0),new Vector3(13,.05f,13),grass);
            Piece(PrimitiveType.Cylinder,"Summoning floor",new Vector3(0,.10f,0),new Vector3(9,.08f,9),floor);
            Piece(PrimitiveType.Cylinder,"Altar lower",new Vector3(0,.24f,0),new Vector3(4,.16f,4),stone);
            Piece(PrimitiveType.Cylinder,"Altar gold lip",new Vector3(0,.43f,0),new Vector3(3.7f,.055f,3.7f),gold);
            Piece(PrimitiveType.Cylinder,"Altar top",new Vector3(0,.50f,0),new Vector3(3.4f,.03f,3.4f),floor);
            for(int i=0;i<12;i++)
            {
                float angle=i*Mathf.PI/6;Vector3 p=new Vector3(Mathf.Cos(angle)*5.5f,0,Mathf.Sin(angle)*5.5f);
                var tile=Piece(PrimitiveType.Cube,"Floor inlay",new Vector3(Mathf.Cos(angle)*2.8f,.2f,Mathf.Sin(angle)*2.8f),new Vector3(.25f,.045f,.7f),teal);
                tile.localRotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,0);
                // Leave the filming side open: foreground columns must never hide the falling orb.
                if(p.z<-.1f)continue;
                Piece(PrimitiveType.Cube,"Column foot",p+Vector3.up*.35f,new Vector3(.95f,.6f,.95f),stone);
                Piece(PrimitiveType.Cylinder,"Sunlit pillar",p+Vector3.up*1.9f,new Vector3(.55f,1.4f,.55f),lightStone);
                Piece(PrimitiveType.Cylinder,"Column capital",p+Vector3.up*3.4f,new Vector3(.95f,.14f,.95f),gold);
                Piece(PrimitiveType.Cylinder,"Column base band",p+Vector3.up*.7f,new Vector3(.7f,.07f,.7f),gold);
                GemPiece("Sanctuary crystal",p+Vector3.up*3.9f,new Vector3(.37f,.65f,.37f),teal);
            }
            // Visible arch behind the altar gives the camera flight parallax and depth.
            Piece(PrimitiveType.Cube,"Sanctuary arch",new Vector3(0,4.8f,5.2f),new Vector3(7,.65f,1),lightStone);
            Material cloud=Material(new Color(.95f,1,1));
            for(int i=0;i<18;i++)
            {
                float a=i*2.399963f;float radius=8+i%4*1.3f;
                Piece(PrimitiveType.Sphere,"Distant cloud",new Vector3(Mathf.Sin(a)*radius,5+i%5*.7f,Mathf.Cos(a)*radius),new Vector3(2.4f,.7f,1.4f),cloud);
            }
            outerRing=Ring("Orbit rune outer",2.45f,.035f,gold);innerRing=Ring("Orbit rune inner",1.8f,.025f,teal);
            orbMaterial=Material(new Color(.3f,.75f,1),.15f);
            orb=Piece(PrimitiveType.Sphere,"Spirit orb",new Vector3(0,3,0),Vector3.one*1.35f,orbMaterial);
            portrait=Piece(PrimitiveType.Quad,"Spirit inside orb",Vector3.zero,Vector3.one,PortraitMaterial());
            portraitMaterial=portrait.GetComponent<Renderer>().sharedMaterial;
            beam=Piece(PrimitiveType.Cylinder,"Release pillar",new Vector3(0,3,0),new Vector3(.25f,3,.25f),Material(new Color(1,.93f,.52f),1));
            beam.gameObject.SetActive(false);
            Material starlight=Material(new Color(1,.9f,.3f),.6f);
            for(int i=0;i<MasterConstants.GetInt("GachaStage3D.moteCount",36);i++)stars.Add(GemPiece("Orbit mote",Vector3.zero,new Vector3(.065f,.13f,.065f),starlight));
            Film(0,0,0);
        }
        Material Material(Color color,float glow=0)
        {var m=new Material(Resources.Load<Shader>("UI/SummonSurface"));m.SetColor("_BaseColor",color);m.SetFloat("_Glow",glow);materials.Add(m);return m;}
        Material PortraitMaterial()
        {var m=new Material(Resources.Load<Shader>("UI/SummonPortrait"));materials.Add(m);return m;}
        Transform Piece(PrimitiveType type,string name,Vector3 position,Vector3 scale,Material material)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.layer=31;obj.transform.SetParent(world.transform,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;
            var collider=obj.GetComponent<Collider>();if(collider!=null)Destroy(collider);
            obj.GetComponent<Renderer>().sharedMaterial=material;return obj.transform;
        }
        Transform Ring(string name,float radius,float tube,Material material)
        {
            const int steps=96,sides=6;var vertices=new Vector3[(steps+1)*(sides+1)];var normals=new Vector3[vertices.Length];var triangles=new int[steps*sides*6];
            for(int i=0;i<=steps;i++)for(int j=0;j<=sides;j++)
            {
                float a=i*Mathf.PI*2/steps,b=j*Mathf.PI*2/sides;int n=i*(sides+1)+j;
                normals[n]=new Vector3(Mathf.Cos(a)*Mathf.Cos(b),Mathf.Sin(b),Mathf.Sin(a)*Mathf.Cos(b));
                vertices[n]=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius)+normals[n]*tube;
                if(i<steps && j<sides){int k=(i*sides+j)*6;triangles[k]=n;triangles[k+1]=n+sides+1;triangles[k+2]=n+1;triangles[k+3]=n+1;triangles[k+4]=n+sides+1;triangles[k+5]=n+sides+2;}
            }
            var mesh=new Mesh{name=name,vertices=vertices,normals=normals,triangles=triangles};mesh.RecalculateBounds();meshes.Add(mesh);
            var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));obj.layer=31;obj.transform.SetParent(world.transform,false);
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<MeshRenderer>().sharedMaterial=material;return obj.transform;
        }
        Transform GemPiece(string name,Vector3 position,Vector3 scale,Material material)
        {
            if(crystal==null)
            {
                var corners=new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.forward,Vector3.right,Vector3.back};
                int[] faces={0,2,3,0,3,4,0,4,5,0,5,2,1,3,2,1,4,3,1,5,4,1,2,5};
                var v=new Vector3[24];var triangles=new int[24];for(int i=0;i<24;i++){v[i]=corners[faces[i]];triangles[i]=i;}
                crystal=new Mesh{name="Summon crystal",vertices=v,triangles=triangles};crystal.RecalculateNormals();crystal.RecalculateBounds();meshes.Add(crystal);
            }
            var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));obj.layer=31;obj.transform.SetParent(world.transform,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;obj.GetComponent<MeshFilter>().sharedMesh=crystal;
            obj.GetComponent<MeshRenderer>().sharedMaterial=material;return obj.transform;
        }
        public void SetSpirit(SpiritDefinition spirit)
        {
            Color color=HubWidgets.Elements[(int)spirit.element];orbMaterial.SetColor("_BaseColor",Color.Lerp(color,Color.white,.25f));
            var sprite=spirit.portrait;
            portraitMaterial.mainTexture=sprite!=null?sprite.texture:null;
            portrait.gameObject.SetActive(sprite!=null);
            if(sprite!=null)
            {
                Rect r=sprite.textureRect;portraitMaterial.mainTextureScale=new Vector2(r.width/sprite.texture.width,r.height/sprite.texture.height);
                portraitMaterial.mainTextureOffset=new Vector2(r.x/sprite.texture.width,r.y/sprite.texture.height);
            }
            portraitMaterial.SetColor("_Color",new Color(1,1,1,.52f));
        }
        public void Show(bool value){world.SetActive(value);screen.gameObject.SetActive(value);}
        // stage 0: crane/orbit shot; stage 1: falling marble; stage 2: release.
        public void Film(int stage,float p,float clock)
        {
            float angle=Mathf.Lerp(-.7f,.05f,Mathf.SmoothStep(0,1,stage==0?p:1));
            float distance=stage==0?Mathf.Lerp(11,7.2f,p):7.2f;
            movieCamera.transform.localPosition=new Vector3(Mathf.Sin(angle)*distance,stage==0?Mathf.Lerp(5.2f,3.3f,p):3.3f,-Mathf.Cos(angle)*distance);
            Vector3 look=new Vector3(0,stage==0?Mathf.Lerp(2.1f,2.7f,p):2.1f,0);
            float y=stage==0?3.5f+Mathf.Sin(clock*1.8f)*.18f:stage==1?Mathf.Lerp(7.5f,1.25f,Mathf.Min(1,p/.72f)*Mathf.Min(1,p/.72f)):
                1.25f;
            if(stage==1 && p>.72f)y+=Mathf.Sin((p-.72f)/.28f*Mathf.PI)*.45f;
            orb.localPosition=new Vector3(0,y,0);orb.localScale=Vector3.one*1.35f*(stage==2?1+p*1.7f:1);
            orb.localRotation=Quaternion.Euler(clock*30,clock*95,0);
            if(stage==2)movieCamera.transform.localPosition+=new Vector3(Mathf.Sin(p*51),Mathf.Cos(p*43),0)*MasterConstants.GetFloat("GachaStage3D.releaseCameraShake",.07f)*(1-p);
            movieCamera.transform.LookAt(Origin+look);
            float normalField=MasterConstants.GetFloat("GachaStage3D.cameraFieldOfView",44);
            movieCamera.fieldOfView=stage==2?Mathf.Lerp(normalField,MasterConstants.GetFloat("GachaStage3D.releaseFieldOfView",36),p):normalField;
            Vector3 toward=(movieCamera.transform.position-orb.position).normalized;
            portrait.position=orb.position+toward*.69f;portrait.rotation=Quaternion.LookRotation(-toward);
            portrait.localScale=Vector3.one*(stage==2?1.1f+p:1.1f);
            innerRing.localPosition=new Vector3(0,stage==0?2.5f:.6f,0);outerRing.localPosition=new Vector3(0,stage==0?3:.62f,0);
            innerRing.localRotation=Quaternion.Euler(stage==0?18:0,clock*55,stage==0?24:0);
            outerRing.localRotation=Quaternion.Euler(stage==0?-20:0,-clock*40,stage==0?-15:0);
            innerRing.localScale=outerRing.localScale=Vector3.one*(stage==2?1+p*1.5f:1);
            beam.gameObject.SetActive(stage==2);beam.localScale=new Vector3(.2f+p*.85f,5,.2f+p*.85f);
            for(int i=0;i<stars.Count;i++)
            {
                float a=i*2.4f+clock*(.6f+i%3*.15f),radius=stage==2?1+p*6:1.8f+i%5*.35f;
                stars[i].localPosition=new Vector3(Mathf.Sin(a)*radius,.8f+Mathf.Repeat(i*.21f+clock*.4f,4),Mathf.Cos(a)*radius);
                stars[i].localRotation=Quaternion.Euler(clock*80,i*30,clock*45);
            }
        }
        void OnDisable(){if(world!=null)world.SetActive(false);}
        void OnDestroy()
        {
            if(movieCamera!=null)movieCamera.targetTexture=null;
            if(target!=null){target.Release();Destroy(target);}
            if(world!=null)Destroy(world);
            foreach(var m in materials)Destroy(m);foreach(var mesh in meshes)Destroy(mesh);
        }
    }
}
