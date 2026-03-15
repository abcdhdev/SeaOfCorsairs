using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
#if UPPipeline
using UnityEngine.Rendering.Universal;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gaia
{
    public class GWS_URPShadowDistance : GWSetting
    {
#if UPPipeline
        [SerializeField]
        float m_originalShadowDistanceValue = 500;
        float m_targetShadowDistanceValue = 500;

        [SerializeField]
        int m_originalShadowResolutionValue = 4096;
        int m_targetShadowResolutionValue = 4096;
#endif

        private void OnEnable()
        {
            m_RPBuiltIn = false;
            m_RPHDRP = false;
            m_RPURP = true;
            m_name = "URP Shadow Settings";
            m_infoTextOK = $"The URP shadow distance, resolution and cascades are adequate for outdoor environments.";
            m_infoTextIssue = $"The system found issues with your shadow cascade setup.";
            m_link = "https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/universalrp-asset.html#shadows";
            m_linkDisplayText = "Shadow settings in the URP Manual";
            m_canRestore = true;
            Initialize();
        }

        public override bool PerformCheck()
        {

#if UPPipeline
            m_infoTextIssue = "The system found the following issues with your shadow cascade setup:\r\n\r\n";

            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
            {
                m_infoTextIssue += "Not using the Universal Render Pipeline, so no checks were performed.\r\n\r\n";
                return true; // Skip checks if not URP
            }

            int score = 0;

            // Shadow Distance
            float shadowDistance = urpAsset.shadowDistance;
            if (shadowDistance < 150)
            {
                score -= 5;
                m_infoTextIssue += "The overall shadow distance is very short (<150) — distant vegetation will not receive shadows.\r\n";
            }
            else if (shadowDistance < 300)
            {
                score -= 2;
                m_infoTextIssue += "The shadow distance is somewhat short (<300) — distant vegetation shadows may look cut off.\r\n";
            }

            // Shadow Resolution
            int resValue = (int)urpAsset.mainLightShadowmapResolution;
            if (resValue <= 2048)
            {
                score -= 5;
                m_infoTextIssue += "The shadow resolution is low (2048 or less) — close-up vegetation shadows will appear pixelated and may flicker.\r\n";
            }
            else if (resValue < 4096)
            {
                score -= 2;
                m_infoTextIssue += "The shadow resolution is moderate (<4096) — close-up shadows may be slightly pixelated.\r\n";
            }

            // Cascade Count
            int cascadeCount = urpAsset.shadowCascadeCount;
            if (cascadeCount < 2)
            {
                score -= 5;
                m_infoTextIssue += "Only one shadow cascade is used — this will cause visible shadow resolution drops with distance.\r\n";
            }
            else if (cascadeCount == 2)
            {
                score -= 2;
                m_infoTextIssue += "Only two shadow cascades are used — shadow transitions may be noticeable in large outdoor scenes.\r\n";
            }

            // Cascade Splits
            if (cascadeCount > 1)
            {
                if (cascadeCount == 2)
                {
                    float firstSplit = urpAsset.cascade2Split;
                    if (firstSplit < 0.005f || firstSplit > 0.07f)
                    {
                        score -= 2;
                        m_infoTextIssue += $"First cascade split ({firstSplit:P1}) is unbalanced — this may waste resolution or reduce close-up shadow detail.\r\n";
                    }
                }
                else if (cascadeCount == 3)
                {
                    Vector2 split3 = urpAsset.cascade3Split;
                    if (split3.x < 0.005f || split3.x > 0.07f)
                    {
                        score -= 2;
                        m_infoTextIssue += $"First cascade split ({split3.x:P1}) is unbalanced — close-up shadows may lose detail.\r\n";
                    }
                    if (split3.y < 0.15f || split3.y > 0.6f)
                    {
                        score -= 2;
                        m_infoTextIssue += $"Second cascade split ({split3.y:P1}) is unbalanced — mid-distance shadows may lose resolution.\r\n";
                    }
                }
                else if (cascadeCount == 4)
                {
                    Vector3 split4 = urpAsset.cascade4Split;
                    if (split4.x < 0.005f || split4.x > 0.07f)
                    {
                        score -= 2;
                        m_infoTextIssue += $"First cascade split ({split4.x:P1}) is unbalanced — close-up shadows may lose detail.\r\n";
                    }
                    if (split4.y < 0.10f || split4.y > 0.6f)
                    {
                        score -= 2;
                        m_infoTextIssue += $"Second cascade split ({split4.y:P1}) is unbalanced — mid-distance shadows may lose resolution.\r\n";
                    }
                    if (split4.z > 0.50f)
                    {
                        score -= 2;
                        m_infoTextIssue += $"Third cascade split ({split4.z:P1}) covers too much distance — distant shadows will lose resolution.\r\n";
                    }
                }
            }

            bool isGood = score > -5;

            if (isGood)
            {
                Status = GWSettingStatus.OK;
            }
            else
            {
                Status = GWSettingStatus.Warning;
            }

            return !isGood;

#else
            Status = GWSettingStatus.OK;
            return false;
#endif
        }

        public override bool FixNow(bool autoFix = false)
        {
#if UNITY_EDITOR && UPPipeline
#if UNITY_6000_0_OR_NEWER
            if (autoFix || EditorUtility.DisplayDialog("Set Shadow Resolution Values?",
            $"Do you want to set the shadow draw distance and resolution and cascade settings in the pipeline asset now?",
            "Continue", "Cancel"))
            {

                UniversalRenderPipelineAsset urpAsset = (UniversalRenderPipelineAsset)GaiaUtils.GetRenderPipelineAsset();

                if (urpAsset != null)
                {
                    m_originalShadowDistanceValue = urpAsset.shadowDistance;
                    urpAsset.shadowDistance = m_targetShadowDistanceValue;

                    m_originalShadowResolutionValue = urpAsset.mainLightShadowmapResolution;
                    urpAsset.mainLightShadowmapResolution = m_targetShadowResolutionValue;

                    urpAsset.shadowCascadeCount = 4;
                    urpAsset.cascade4Split = new Vector3(0.015f, 0.15f, 0.50f);

                    EditorUtility.SetDirty(urpAsset);
                    
                    PerformCheck();
                    m_foldedOut = false;
                    return true;
                }
                else
                {
                    Debug.LogError("Error while accessing the URP Render Pipeline asset - is there a URP render pipeline asset assigned in the Project > Graphics settings?");
                }

            }
#else
                    if (!autoFix)
                    { 
                        EditorUtility.DisplayDialog("Auto-Fix not possible!","Gaia can only auto-fix this issue in Unity 6 or higher, because some of the render pipeline asset settings are not accessible via script in earlier versions\r\n\r\n. " +
                        "To fix this issue manually, please take a look at the Shadow settings in your render pipeline asset and set up the following values:\r\n\r\n" +
                        "Shadow Resolution: 4096 or higher\r\n" +
                        "Max Shadow Distance: 500\r\n\r\n" +
                        "Max Shadow Distance: 4 Shadow cascades\r\n\r\n" +
                        "1st cascade: 1.5%" +
                        "2nd cascade: 15%" +
                        "3rd cascade: 50%", "OK");
                    }
#endif
#endif
            return false;
        }

        public override string GetOriginalValueString()
        {
#if UPPipeline
            return $"Distance: {m_originalShadowDistanceValue}, Resolution: {m_originalShadowResolutionValue}";
#else
            return "URP Pipeline not found!";
#endif
        }

        public override bool RestoreOriginalValue()
        {
#if UNITY_6000_0_OR_NEWER && UNITY_EDITOR && UPPipeline
            if (EditorUtility.DisplayDialog("Restore Shadow Draw Distance?",
            $"Do you want to restore he shadow draw distance, resolution and cascade settings to their original values {GetOriginalValueString()} in the render pipeline asset now?",
            "Continue", "Cancel"))
            {
                UniversalRenderPipelineAsset urpAsset = (UniversalRenderPipelineAsset)GaiaUtils.GetRenderPipelineAsset();

                if (urpAsset != null)
                {
                    urpAsset.shadowDistance = m_originalShadowDistanceValue;
                    urpAsset.mainLightShadowmapResolution = m_originalShadowResolutionValue;
                    return true;
                }
                else
                {
                    Debug.LogError("Error while accessing the URP Render Pipeline asset - is there a URP render pipeline asset assigned in the Project > Graphics settings?");
                }
                return false;
            }
#endif
            return false;
        }


    }
}
