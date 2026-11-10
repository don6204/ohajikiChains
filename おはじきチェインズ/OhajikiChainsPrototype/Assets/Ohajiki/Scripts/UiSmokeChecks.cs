using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Ohajiki
{
    // Called only by explicit smoke modes. Verify decoration does not intercept input.
    public static class UiSmokeChecks
    {
        public static void Validate(GameObject root)
        {
            if(UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length!=0)Fail("Unexpected SE source");
            if(!root.activeInHierarchy)Fail("Check must target the active screen");
            var battle=root.GetComponent<BattleView>();
            if(battle!=null && battle.OverlayVisible)root=root.transform.Find("Screen/Overlay").gameObject;
            var events=EventSystem.current;if(events==null)Fail("Missing EventSystem");
            var hits=new List<RaycastResult>();int tested=0;
            foreach(var button in root.GetComponentsInChildren<UnityEngine.UI.Button>())
            {
                if(!button.gameObject.activeInHierarchy || !button.IsInteractable())continue;
                var rect=(RectTransform)button.transform;
                Vector2 point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
                if(point.x<0 || point.x>=Screen.width || point.y<0 || point.y>=Screen.height)continue;
                var scroll=button.GetComponentInParent<UnityEngine.UI.ScrollRect>();
                if(scroll!=null&&!RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport,point,null))continue;
                hits.Clear();events.RaycastAll(new PointerEventData(events){position=point},hits);
                if(hits.Count==0 || !hits[0].gameObject.transform.IsChildOf(button.transform))Fail("Button center blocked: "+button.name);
                tested++;
            }
            foreach(var text in root.GetComponentsInChildren<TMP_Text>())
                if(text.isTextOverflowing)Debug.LogWarning("UI OVERFLOW: "+text.text.Replace('\n',' '));
            Debug.Log($"UI CHECK: {tested} visible buttons raycast correctly, no SE, {Screen.width}x{Screen.height}");
        }
        static void Fail(string message){Debug.LogError("UI CHECK FAIL: "+message);Application.Quit(24);throw new InvalidOperationException(message);}
    }
}
