using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Procedural animation for the PS1 puppets - no clips, no Animator, no
    /// imported rigs. Drives a swinging walk cycle, head bob, blinking, eye
    /// darts and stress fidgets off the transform hierarchy built by
    /// CharacterBuilder. Cheap enough to run on 8 players + 6 NPCs.
    /// Stress is the readable tell channel (GDD 04): as it rises the puppet
    /// fidgets, blinks faster and its eyes dart - no numbers on screen.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        [Range(0f, 1f)] public float Stress;
        public Transform LookTarget;

        private CharacterBuilder.Rig _rig;
        private float _phase, _speed01, _blinkTimer, _blinkT, _dartTimer;
        private Vector2 _dart;
        private Vector3 _prevPos;
        private float _seedOffset;

        public void Init(CharacterBuilder.Rig rig, int seed)
        {
            _rig = rig;
            _seedOffset = (seed % 100) * 0.37f;   // desync everyone's idle
            _prevPos = transform.position;
            _blinkTimer = 1f + (seed % 7) * 0.3f;
        }

        /// <summary>Feed movement speed directly (players); NPCs infer it from position.</summary>
        public void SetSpeed(float metresPerSecond) => _speed01 = Mathf.Clamp01(metresPerSecond / 6f);

        private void LateUpdate()
        {
            if (_rig == null) return;
            float dt = Time.deltaTime;
            float t = Time.time + _seedOffset;

            // infer speed when nobody feeds it
            Vector3 delta = transform.position - _prevPos;
            delta.y = 0f;
            _prevPos = transform.position;
            float measured = dt > 0f ? delta.magnitude / dt : 0f;
            if (measured > 20f) measured = 0f;   // teleport (bell/spawn), not running
            if (measured > 0.05f) _speed01 = Mathf.Lerp(_speed01, Mathf.Clamp01(measured / 6f), dt * 8f);
            else _speed01 = Mathf.Lerp(_speed01, 0f, dt * 8f);

            // ---- walk cycle ----
            _phase += dt * Mathf.Lerp(2.2f, 9.5f, _speed01) * (_speed01 > 0.01f ? 1f : 0f);
            float swing = Mathf.Sin(_phase) * Mathf.Lerp(0f, 42f, _speed01);
            float counter = Mathf.Sin(_phase + Mathf.PI) * Mathf.Lerp(0f, 34f, _speed01);

            if (_rig.LegL) _rig.LegL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (_rig.LegR) _rig.LegR.localRotation = Quaternion.Euler(counter, 0f, 0f);

            // arms swing opposite the legs, plus a nervous idle sway
            float idleSway = Mathf.Sin(t * 1.3f) * 3f + Stress * Mathf.Sin(t * 11f) * 6f;
            if (_rig.ArmL) _rig.ArmL.localRotation = Quaternion.Euler(counter * 0.8f + idleSway, 0f, 6f);
            if (_rig.ArmR) _rig.ArmR.localRotation = Quaternion.Euler(swing * 0.8f - idleSway, 0f, -6f);

            // ---- body bob + breathing ----
            if (_rig.TorsoT)
            {
                float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.035f * _speed01;
                float breathe = Mathf.Sin(t * 1.6f) * 0.008f;
                _rig.TorsoT.localPosition = new Vector3(0f, 1.12f + bob + breathe, 0f);
                _rig.TorsoT.localRotation = Quaternion.Euler(_speed01 * 5f, 0f, Mathf.Sin(_phase) * 2.5f * _speed01);
            }

            // ---- head: look target, bob, stress twitch ----
            if (_rig.HeadPivot)
            {
                Quaternion want = Quaternion.identity;
                if (LookTarget != null)
                {
                    Vector3 dir = LookTarget.position - _rig.HeadPivot.position;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        var world = Quaternion.LookRotation(dir);
                        want = Quaternion.Inverse(transform.rotation) * world;
                        // never snap the neck: clamp to a human cone
                        Vector3 e = want.eulerAngles;
                        float yaw = Mathf.DeltaAngle(0f, e.y), pitch = Mathf.DeltaAngle(0f, e.x);
                        want = Quaternion.Euler(Mathf.Clamp(pitch, -25f, 30f), Mathf.Clamp(yaw, -70f, 70f), 0f);
                    }
                }
                float twitch = Stress * Mathf.Sin(t * 17f) * 2.5f;
                float nod = Mathf.Sin(_phase * 2f) * 2f * _speed01;
                _rig.HeadPivot.localRotation = Quaternion.Slerp(_rig.HeadPivot.localRotation,
                    want * Quaternion.Euler(nod + twitch, twitch * 0.6f, 0f), dt * 6f);
            }

            // ---- blinking (faster under stress) ----
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinkT = 1f;
                _blinkTimer = Random.Range(1.6f, 4.5f) * Mathf.Lerp(1f, 0.35f, Stress);
            }
            _blinkT = Mathf.Max(0f, _blinkT - dt * 9f);
            float lid = 1f - _blinkT;
            if (_rig.EyeL) _rig.EyeL.localScale = new Vector3(0.30f, 0.34f * Mathf.Max(0.06f, lid), 0.06f);
            if (_rig.EyeR) _rig.EyeR.localScale = new Vector3(0.30f, 0.34f * Mathf.Max(0.06f, lid), 0.06f);

            // ---- eye darts: the liar's tell ----
            _dartTimer -= dt;
            if (_dartTimer <= 0f)
            {
                _dartTimer = Random.Range(0.7f, 2.6f) * Mathf.Lerp(1.4f, 0.3f, Stress);
                float range = Mathf.Lerp(0.12f, 0.30f, Stress);
                _dart = new Vector2(Random.Range(-range, range), Random.Range(-range * 0.6f, range * 0.6f));
            }
            if (_rig.PupilL) _rig.PupilL.localPosition = Vector3.Lerp(_rig.PupilL.localPosition,
                new Vector3(_dart.x, _dart.y, 0.6f), dt * 8f);
            if (_rig.PupilR) _rig.PupilR.localPosition = Vector3.Lerp(_rig.PupilR.localPosition,
                new Vector3(_dart.x, _dart.y, 0.6f), dt * 8f);

            // ---- jaw: talking is driven externally by SetTalking() ----
            if (_rig.Jaw)
            {
                float open = _talking > 0f ? Mathf.Abs(Mathf.Sin(t * 18f)) * 0.10f : 0f;
                _talking = Mathf.Max(0f, _talking - dt);
                _rig.Jaw.localPosition = new Vector3(0f, -0.30f - open, 0.30f);
            }
        }

        private float _talking;
        public void SetTalking(float seconds) => _talking = seconds;
    }
}
