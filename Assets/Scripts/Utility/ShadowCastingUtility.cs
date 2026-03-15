using UnityEngine;
using UnityEngine.Rendering;

namespace SeaWars.Utility
{
    internal static class ShadowCastingUtility
    {
        public static void DisableShadowCastingInChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    renderer.shadowCastingMode == ShadowCastingMode.Off ||
                    !ShouldDisableShadowCasting(renderer))
                {
                    continue;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static bool ShouldDisableShadowCasting(Renderer renderer)
        {
            return renderer is ParticleSystemRenderer ||
                   renderer is TrailRenderer ||
                   renderer is LineRenderer;
        }
    }
}
