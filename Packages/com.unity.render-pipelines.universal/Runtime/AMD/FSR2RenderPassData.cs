using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using System;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices.WindowsRuntime;

public enum FSR2Mode
{
    Disabled = 0,
    Quality,
    Balanced,
    Performance,
    UltraPerformance,
    [InspectorName(null)]
    Max
}
public class FSR2RenderPassData : MonoBehaviour
{
    struct ModeData
    {
        public float render_scale;
        public float mipmap_bias;
    }
    readonly ModeData[] mode_data_ = new ModeData[(int)FSR2Mode.Max]
    {
        new(){render_scale = 1.0f, mipmap_bias = 0.0f},
        new(){render_scale = 1.0f / 1.5f, mipmap_bias = -1.58f},
        new(){render_scale = 1.0f / 1.7f, mipmap_bias = -1.76f},
        new(){render_scale = 1.0f / 2.0f, mipmap_bias= -2.0f},
        new(){render_scale = 1.0f / 3.0f , mipmap_bias = -2.58f}
    };

    public FSR2Mode FSR2Quality = FSR2Mode.Disabled;

    [HideInInspector]
    public AMDFFX.FSR2Pass.FSR2InitParam.FlagBits FSR2ContextFlags;

    [Header("Sharpening")]
    public bool EnableSharpening = true;
    [Range(0f, 1f)]
    public float Sharpness = .3f;

    [Header("Reactive Mask")]
    public bool OutputReactiveMask = true;
    [Range(0.0f, 1.0f)]
    public float ReactiveMaskScale = .0f;
    [Range(0.0f, 1.0f)]
    public float CutoffThreshold = 1.0f;
    [Range(0.0f, 1.0f)]
    public float BinaryValue = 1.0f;
    public AMDFFX.FSR2Pass.FSR2GenReactiveParam.FlagBits ReactiveMaskFlags;
    [HideInInspector]
    public RenderTexture OptReactiveMaskTex;
    [HideInInspector]
    public RenderTexture OptTransparencyAndCompositionTex;

    UniversalRenderPipelineAsset pipeline_asset_;

    Camera camera;
    bool originCameraAllowMSAA = false;
    void Start()
    {
        camera = GetComponent<Camera>();
        if (camera == null)
            Debug.LogError("FSR2PassData should be added on the GameObject that has Camera");
        originCameraAllowMSAA = camera.allowMSAA;
        pipeline_asset_ = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline_asset_ == null)
            Debug.LogError("no UniversalRenderPipelineAsset used in current pipeline.");

        if (!FSR2Feature.IsSupported())
        {
            Debug.LogError("FSR2 is not supported on current platform.");
            FSR2Quality = FSR2Mode.Disabled;
            this.enabled = false;
        }
    }

    FSR2Mode last_fsr2_mode_ = FSR2Mode.Max;

    void Update()
    {
        if (last_fsr2_mode_ != FSR2Quality)
        {
            if (FSR2Quality >= FSR2Mode.Disabled && FSR2Quality <= FSR2Mode.UltraPerformance)
            {
                camera.allowMSAA = false;
                ref var current_mode_data = ref mode_data_[(int)FSR2Quality];
                if (FSR2Quality != FSR2Mode.Disabled)
                    Shader.EnableKeyword("_AMD_FSR2");
                else
                {
                    camera.allowMSAA = originCameraAllowMSAA;
                    Shader.DisableKeyword("_AMD_FSR2");
                }
                pipeline_asset_.renderScale = current_mode_data.render_scale;
                Shader.SetGlobalFloat("amd_fsr2_mipmap_bias", current_mode_data.mipmap_bias);
                last_fsr2_mode_ = FSR2Quality;
            }
        }
    }
}
