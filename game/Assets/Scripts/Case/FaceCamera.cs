using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>Billboard toward the local camera (own file for prefab-safe binding).</summary>
    public class FaceCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
