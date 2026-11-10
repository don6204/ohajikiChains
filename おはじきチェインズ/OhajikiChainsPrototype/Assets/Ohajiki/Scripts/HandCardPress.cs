using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ohajiki
{
    public sealed class HandCardPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action Pressed, Released;
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Pressed?.Invoke(); }
        public void OnPointerUp(PointerEventData e) => Released?.Invoke();
        public void OnPointerExit(PointerEventData e) => Released?.Invoke();
        void OnDisable() => Released?.Invoke();
    }
}
