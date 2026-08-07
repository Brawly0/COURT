using System.Collections.Generic;
using UnityEngine;

namespace CaseClosed.Game.Prototype
{
    /// <summary>
    /// Hides the parts of YOUR OWN body that would otherwise be inside your face.
    ///
    /// TWO RULES THIS OBEYS, BOTH LOAD-BEARING:
    ///
    /// 1. It only ever toggles <c>Renderer.enabled</c>. It never deactivates the
    ///    GameObject. Deactivating would take NetworkBehaviours, colliders, the
    ///    carry socket and the AudioSource carrying this player's voice down with
    ///    it — a rendering decision silently becoming a gameplay and networking one.
    ///
    /// 2. It is only ever called on the LOCAL player, by that player's own camera
    ///    rig. Remote copies are never touched, so everyone else keeps seeing a
    ///    whole person regardless of which camera mode that person is using.
    /// </summary>
    public class PlayerLocalBody : MonoBehaviour
    {
        [Tooltip("Joints whose entire subtree is hidden in first person. The head sits " +
                 "directly on the eye point, so it always belongs here.")]
        public string[] HideInFirstPerson = { "Visual/Hips/Torso/Head" };

        /// <summary>False while first-person hiding is applied. Debug readout.</summary>
        public bool LocalBodyVisible { get; private set; } = true;

        private readonly List<Renderer> _hidden = new();
        private bool _resolved;

        /// <summary>
        /// Renderers are resolved once, lazily — the rig may call this before or
        /// after the network spawn has finished building the hierarchy.
        /// </summary>
        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            foreach (string path in HideInFirstPerson)
            {
                var joint = transform.Find(path);
                if (joint == null)
                {
                    Debug.LogWarning($"[LocalBody] No joint at '{path}' — nothing to hide there.");
                    continue;
                }
                _hidden.AddRange(joint.GetComponentsInChildren<Renderer>(true));
            }
        }

        public void SetFirstPerson(bool firstPerson)
        {
            Resolve();

            bool visible = !firstPerson;
            if (LocalBodyVisible == visible) return;   // already in that state

            foreach (var renderer in _hidden)
                if (renderer != null) renderer.enabled = visible;

            LocalBodyVisible = visible;
        }

        /// <summary>Belt and braces: never leave a body invisible on teardown.</summary>
        private void OnDisable() => SetFirstPerson(false);
    }
}
