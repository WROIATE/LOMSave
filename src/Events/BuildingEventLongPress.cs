using Mortal.Free;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LOMSave.Probe
{
    internal sealed class BuildingEventLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        private const float HoldSeconds = 0.5f;
        private PositionController _controller;
        private bool _holding;
        private float _startedAt;
        internal bool SuppressClick { get; private set; }

        internal void Bind(PositionController controller) { _controller = controller; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Middle && EventInspector.CanPreview(_controller))
                EventInspector.OpenBuilding(_controller);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            SuppressClick = false;
            _holding = EventInspector.CanPreview(_controller);
            _startedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            // Handle a release after the threshold even if it arrives before this frame's Update.
            TryOpen();
            CancelHold();
        }

        public void OnPointerExit(PointerEventData eventData) { CancelHold(); }
        private void OnDisable() { CancelHold(); }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelHold(); }

        private void CancelHold()
        {
            _holding = false;
            OptionImpactPreview.CancelBuildingHold(this);
        }

        private void Update()
        {
            if (!_holding) return;
            if (!Input.GetMouseButton(0) || !EventInspector.CanPreview(_controller))
            {
                CancelHold();
                return;
            }
            var elapsed = Time.unscaledTime - _startedAt;
            if (PreviewInteraction.ShowHoldProgress(elapsed))
                OptionImpactPreview.SetBuildingHoldProgress(this, elapsed / HoldSeconds);
            TryOpen();
        }

        private void TryOpen()
        {
            if (!_holding || Time.unscaledTime - _startedAt < HoldSeconds) return;
            CancelHold();
            // Keep this through pointer exit/up: opening an overlay changes the raycast target.
            // Only the next left-button press may make a normal building click eligible again.
            SuppressClick = true;
            EventInspector.OpenBuilding(_controller);
        }
    }
}
