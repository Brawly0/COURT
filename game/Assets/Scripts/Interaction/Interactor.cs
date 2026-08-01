using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game
{
    public interface IInteractable
    {
        string Prompt { get; }
        void Interact();
    }

    /// <summary>
    /// Hold-E interaction (GDD 10: taps misfire under stress). Raycasts from
    /// the camera; exposes prompt + hold progress for the HUD.
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        public float Range = 3.0f;
        public float HoldSeconds = 0.6f;

        public string CurrentPrompt { get; private set; }
        public float HoldProgress { get; private set; }

        private Camera _cam;
        private IInteractable _target;
        private float _held;

        private void Awake() => _cam = GetComponentInChildren<Camera>();

        private void Update()
        {
            _target = null;
            CurrentPrompt = null;

            var ray = new Ray(_cam.transform.position, _cam.transform.forward);
            if (Physics.Raycast(ray, out var hit, Range))
            {
                _target = hit.collider.GetComponentInParent<IInteractable>();
                if (_target != null) CurrentPrompt = _target.Prompt;
            }

            var kb = Keyboard.current;
            bool holding = kb != null && kb.eKey.isPressed && _target != null;
            _held = holding ? _held + Time.deltaTime : 0f;
            HoldProgress = Mathf.Clamp01(_held / HoldSeconds);

            if (_held >= HoldSeconds && _target != null)
            {
                _held = 0f;
                _target.Interact();
            }
        }
    }
}
