using UnityEngine;
using UnityEngine.EventSystems;
namespace Ohajiki
{
    public sealed class UiButtonMotion : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler
    {
        UnityEngine.UI.Button button;
        bool over,pressed;
        void Awake()=>button=GetComponent<UnityEngine.UI.Button>();
        public void OnPointerEnter(PointerEventData e)=>over=true;
        public void OnPointerExit(PointerEventData e){over=false;pressed=false;}
        public void OnPointerDown(PointerEventData e)=>pressed=true;
        public void OnPointerUp(PointerEventData e)=>pressed=false;
        void Update()
        {
            float scale=button!=null&&button.IsInteractable()?(pressed?.96f:over?1.025f:1):1;
            transform.localScale=Vector3.Lerp(transform.localScale,Vector3.one*scale,1-Mathf.Exp(-20*Time.unscaledDeltaTime));
        }
        void OnDisable(){over=pressed=false;transform.localScale=Vector3.one;}
    }
}
