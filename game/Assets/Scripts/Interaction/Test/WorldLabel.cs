using UnityEngine;

namespace CaseClosed.Game.Interaction.Test
{
    /// <summary>
    /// Tiny world-space text helper so test objects can show their own state without
    /// a Canvas. Development scaffolding, not shipping UI.
    /// </summary>
    internal static class WorldLabel
    {
        private static GUIStyle _style;

        public static void Draw(Vector3 worldPosition, string text)
        {
            var camera = Camera.main;
            if (camera == null) return;

            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) return;

            // Only legible up close; further out it is just noise over the scene.
            if (screen.z > 22f) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                { fontSize = 11, alignment = TextAnchor.MiddleCenter };
                _style.normal.textColor = new Color(1f, 0.95f, 0.8f, 0.9f);
            }

            GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y - 16f, 160f, 34f), text, _style);
        }
    }
}
