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
        public int LookSeed;

        private CharacterAnimator _anim;
        private Vector3 _target;
        private float _retarget, _lookTimer;
        private Transform _watching;

        private void Start()
        {
            // Build the rig HERE, at runtime. An editor-baked rig doesn't
            // survive entering play mode: CharacterAnimator's rig reference
            // isn't serializable, so baked NPCs woke as frozen mannequins.
            // Only the seed is serialized - same seed, same Greg, every run.
            var stale = transform.Find("CharacterBody");
            if (stale != null) Destroy(stale.gameObject);

            _anim = GetComponent<CharacterAnimator>();
            if (_anim == null) _anim = gameObject.AddComponent<CharacterAnimator>();
            var rig = CharacterBuilder.Build(transform, LookSeed, false);
            _anim.Init(rig, LookSeed);

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
                    if (d < best)
                    {
                        best = d;
                        // aim at the EYES, not the root - the root pivot is at
                        // floor level and made every NPC stare at players' shoes
                        var cam = p.GetComponentInChildren<Camera>(true);
                        _watching = cam != null ? cam.transform : p.transform;
                    }
                }
            }
            if (_anim != null) _anim.LookTarget = _watching;
        }
    }
}
