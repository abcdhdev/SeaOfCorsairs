using System;
using UnityEngine;

namespace Effects
{
    public class BoatFoamGenerator : MonoBehaviour
    {
        private const float DefaultPixelsPerUnit = 100f;

        public Transform boatTransform;
        public ParticleSystem ps;

        [Header("Wake Ripple Flipbook")]
        [SerializeField] private bool configureRippleFlipbook = true;
        [SerializeField] private string rippleResourceFolder = "WakeRipples";
        [SerializeField] private float rippleFramesPerSecond = 12f;
        [SerializeField] private bool matchLifetimeToFlipbook = true;

        private ParticleSystem.MainModule _module;
        private Vector3 _offset;
        private static Sprite[] s_cachedRippleSprites;

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
            ConfigureRippleFlipbook();
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

        private void ConfigureRippleFlipbook()
        {
            if (!configureRippleFlipbook)
            {
                return;
            }

            Sprite[] rippleSprites = LoadRippleSprites();
            if (rippleSprites.Length == 0)
            {
                return;
            }

            var textureSheet = ps.textureSheetAnimation;
            textureSheet.enabled = true;
            textureSheet.mode = ParticleSystemAnimationMode.Sprites;
            textureSheet.timeMode = ParticleSystemAnimationTimeMode.FPS;
            textureSheet.fps = rippleFramesPerSecond;
            textureSheet.cycleCount = 1;

            while (textureSheet.spriteCount > 0)
            {
                textureSheet.RemoveSprite(textureSheet.spriteCount - 1);
            }

            foreach (Sprite rippleSprite in rippleSprites)
            {
                textureSheet.AddSprite(rippleSprite);
            }

            if (matchLifetimeToFlipbook && rippleFramesPerSecond > 0.01f)
            {
                _module.startLifetime = rippleSprites.Length / rippleFramesPerSecond;
            }

            ps.Clear(true);
            if (Application.isPlaying)
            {
                ps.Play(true);
            }
        }

        private Sprite[] LoadRippleSprites()
        {
            if (s_cachedRippleSprites != null && s_cachedRippleSprites.Length > 0)
            {
                return s_cachedRippleSprites;
            }

            Texture2D[] rippleTextures = Resources.LoadAll<Texture2D>(rippleResourceFolder);
            if (rippleTextures == null || rippleTextures.Length == 0)
            {
                return Array.Empty<Sprite>();
            }

            Array.Sort(rippleTextures, (left, right) => string.CompareOrdinal(left.name, right.name));

            s_cachedRippleSprites = new Sprite[rippleTextures.Length];
            for (int i = 0; i < rippleTextures.Length; i++)
            {
                Texture2D rippleTexture = rippleTextures[i];
                s_cachedRippleSprites[i] = Sprite.Create(
                    rippleTexture,
                    new Rect(0f, 0f, rippleTexture.width, rippleTexture.height),
                    new Vector2(0.5f, 0.5f),
                    DefaultPixelsPerUnit);
            }

            return s_cachedRippleSprites;
        }
    }
}
