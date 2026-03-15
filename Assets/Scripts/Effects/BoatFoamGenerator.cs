using UnityEngine;

namespace Effects
{
    public class BoatFoamGenerator : MonoBehaviour
    {
        public Transform boatTransform;
        private ParticleSystem.MainModule _module;
        public ParticleSystem ps;
        private Vector3 _offset;

        private void Start()
        {
            if (ps == null)
            {
                ps = GetComponent<ParticleSystem>();
            }

            if (ps == null)
            {
                enabled = false;
                return;
            }

            _module = ps.main;
            _offset = transform.localPosition;
        }

        private void Update()
        {
            if (boatTransform == null)
            {
                return;
            }

            var pos = boatTransform.TransformPoint(_offset);
            pos.y = 10f;
            transform.position = pos;

            var fwd = boatTransform.forward;
            fwd.y = 0;
            var angle = Vector3.Angle(fwd.normalized, Vector3.forward);
            _module.startRotation = angle * Mathf.Deg2Rad;
        }
    }
}
