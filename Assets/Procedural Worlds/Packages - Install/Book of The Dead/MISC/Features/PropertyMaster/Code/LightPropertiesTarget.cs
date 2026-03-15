using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if HDPipeline
using UnityEngine.Rendering.HighDefinition;
#elif UPPipeline
using UnityEngine.Rendering.Universal;
#endif

[ExecuteAlways]
public class LightPropertiesTarget : MonoBehaviour
{
    public static Light Instance;
#if HDPipeline
    public static HDAdditionalLightData InstanceHD;
#elif UPPipeline
    public static UniversalAdditionalLightData InstanceURP;
#endif

    void OnEnable()
    {
        Instance = GetComponent<Light>();
#if HDPipeline
        InstanceHD = GetComponent<HDAdditionalLightData>();
#elif UPPipeline
        InstanceURP = GetComponent<UniversalAdditionalLightData>();
#endif
    }
}
