using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Placeholder NPC behaviour: loiters near its post, glances at whoever
    /// walks past, occasionally shifts its weight. Enough life that the
    /// courthouse doesn't feel like a morgue. Replaced by the real schedule/
    /// witness AI (GDD 04) when interviews land.
    /// </summary>
    public class NpcIdle : MonoBehaviour
    {
        public Vector3 HomePosition;
        public float WanderRadius = 1.6f;
        public float NoticeRange = 7f;

        private CharacterAnimator _anim;
        private Vector3 _target;
        private float _retarget, _lookTimer;
        private Transform _watching;

        private void Start()
        {
            _anim = GetComponent<CharacterAnimator>();
            if (HomePosition == Vector3.zero) HomePosition = transform.position;
            _target = HomePosition;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // shuffle around the post now and then
            _retarget -= dt;
            if (_retarget <= 0f)
            {
                _retarget = Random.Range(4f, 11f);
                var off = Random.insideUnitCircle * WanderRadius;
                _target = HomePosition + new Vector3(off.x, 0f, off.y);
            }
            Vector3 flat = _target - transform.position;
            flat.y = 0f;
            if (flat.magnitude > 0.15f)
            {
                transform.position += flat.normalized * Mathf.Min(0.85f * dt, flat.magnitude);
                var face = Quaternion.LookRotation(flat);
                transform.rotation = Quaternion.Slerp(transform.rotation, face, dt * 3f);
            }

            // track the nearest player - being watched is half the tension
            _lookTimer -= dt;
            if (_lookTimer <= 0f)
            {
                _lookTimer = 0.5f;
                _watching = null;
                float best = NoticeRange * NoticeRange;
                foreach (var p in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
                {
                    float d = (p.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _watching = p.transform; }
                }
            }
            if (_anim != null) _anim.LookTarget = _watching;
        }
    }
}
