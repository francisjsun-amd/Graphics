using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices;
using System;
using UnityEngine.XR;

public class FSR2RenderPass : ScriptableRenderPass
{
    string inputTextureColorName = "_CameraColorTexture";
    string inputTextureDepthName = "_CameraDepthTexture";
    string inputTextureMotionVectorName = "_MotionVectorTexture";
    string inputTextureOpaqueName = "_CameraOpaqueTexture";

    private int texIDColor;
    private int texIDDepth;
    private int texIDMotionVector;
    private int texIDOpaque;

    private FSR2RenderPassData fsr2PassData;

    public FSR2RenderPass()
    {
        texIDColor = Shader.PropertyToID(inputTextureColorName);
        texIDDepth = Shader.PropertyToID(inputTextureDepthName);
        texIDMotionVector = Shader.PropertyToID(inputTextureMotionVectorName);
        texIDOpaque = Shader.PropertyToID(inputTextureOpaqueName);
    }
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        ConfigureInput(ScriptableRenderPassInput.Motion | ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
    }

    private RenderTexture texOutput;

    private void CreateUAVRes(int width, int height, RenderTextureFormat render_tex_fmt, bool is_rgb, ref RenderTexture out_tex)
    {
        if (out_tex != null) { out_tex.Release(); }
        out_tex = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, render_tex_fmt) { sRGB = is_rgb, enableRandomWrite = true });
        out_tex.Create();
    }

    bool display_size_changed_ = true;
    bool initialized_ = false;
    public void Initialize(CommandBuffer cb, ref RenderingData renderingData)
    {
        ref var camera_data = ref renderingData.cameraData;
        var camera = camera_data.camera;
        UInt32 flags = (UInt32)(
            AMDFFX.FSR2Pass.FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_AUTO_EXPOSURE
            | AMDFFX.FSR2Pass.FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_MOTION_VECTORS_JITTER_CANCELLATION
            | AMDFFX.FSR2Pass.FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_DEPTH_INVERTED
            );
        if (camera_data.isHdrEnabled)
            flags |= (UInt32)AMDFFX.FSR2Pass.FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_HIGH_DYNAMIC_RANGE;
        AMDFFX.FSR2Pass.Initialzie(cb, camera, flags);
        initialized_ = true;
    }

    private RenderTexture texOutputReactive;
    private int[] lastRenderSize = new int[2] { 0, 0 };
    public void ExecuteForReactiveMask(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (fsr2PassData.FSR2Quality != FSR2Mode.Disabled)
        {
            if (fsr2PassData.OutputReactiveMask)
            {
                ref var cameraData = ref renderingData.cameraData;
                var camera = cameraData.camera;

                var texOpaque = Shader.GetGlobalTexture(texIDOpaque);
                // var texColor = Shader.GetGlobalTexture("_CameraColorTexture");
                UniversalRenderer uRenderer = cameraData.renderer as UniversalRenderer;
                var texColor = Shader.GetGlobalTexture(uRenderer.activeCameraColorAttachment.id);

                if (texColor == null || texOpaque == null)
                    return;

                // create uav for texReactive
                int renderSizeWidth = (int)(camera.pixelWidth * cameraData.renderScale);
                int renderSizeHeight = (int)(camera.pixelHeight * cameraData.renderScale);
                if (lastRenderSize[0] != renderSizeWidth || lastRenderSize[1] != renderSizeHeight)
                {
                    CreateUAVRes(renderSizeWidth, renderSizeHeight, RenderTextureFormat.R8, false, ref texOutputReactive);
                    lastRenderSize[0] = renderSizeWidth;
                    lastRenderSize[1] = renderSizeHeight;
                }

                CommandBuffer cb = CommandBufferPool.Get();
                cb.SetRenderTarget(RenderTargetHandle.CameraTarget.id);
                Initialize(cb, ref renderingData);

                AMDFFX.FSR2Pass.UpdateTexture(cb, texOpaque, AMDFFX.TextureName.FSR2_COLOR_OPAQUE_ONLY);
                AMDFFX.FSR2Pass.UpdateTexture(cb, texColor, AMDFFX.TextureName.FSR2_COLOR_PRE_UPSCALE);
                AMDFFX.FSR2Pass.UpdateTexture(cb, texOutputReactive, AMDFFX.TextureName.FSR2_REACTIVE);
                AMDFFX.FSR2Pass.FSR2GenReactiveParam genReactiveParam = new AMDFFX.FSR2Pass.FSR2GenReactiveParam()
                {
                    renderSize = new UInt32[2] { (UInt32)renderSizeWidth, (UInt32)renderSizeHeight },
                    scale = fsr2PassData.ReactiveMaskScale,
                    cutoffThreshold = fsr2PassData.CutoffThreshold,
                    binaryValue = fsr2PassData.BinaryValue,
                    flags = (UInt32)fsr2PassData.ReactiveMaskFlags
                };
                AMDFFX.FSR2Pass.GenReactiveMask(cb, ref genReactiveParam);

                context.ExecuteCommandBuffer(cb);
                cb.Release();
            }
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (fsr2PassData.FSR2Quality != FSR2Mode.Disabled)
        {
            ref var cameraData = ref renderingData.cameraData;
            UniversalRenderer uRenderer = cameraData.renderer as UniversalRenderer;
            var texColor = Shader.GetGlobalTexture(uRenderer.fsr2TexColorID) as RenderTexture;
            // var texColor = Shader.GetGlobalTexture(texIDColor) as RenderTexture;
            if (texColor == null)
                return;
            var camera = cameraData.camera;
            if (display_size_changed_)
            {
                CreateUAVRes(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.ARGB32, texColor.sRGB, ref texOutput);
                display_size_changed_ = false;
            }
            // var texDepth = Shader.GetGlobalTexture(texIDDepth);
            var texDepth = Shader.GetGlobalTexture(RenderTargetHandle.CameraTarget.id); // TODO
            var texMotionVector = Shader.GetGlobalTexture(texIDMotionVector);
            var texOpaque = Shader.GetGlobalTexture(texIDOpaque);
            if (texDepth == null || texMotionVector == null || texOpaque == null)
                return;

            int renderSizeWidth = (int)(camera.pixelWidth * cameraData.renderScale);
            int renderSizeHeight = (int)(camera.pixelHeight * cameraData.renderScale);
            RenderTexture texReactive, texTransparencyAndComposition;
            if (fsr2PassData.OutputReactiveMask)
            {
                texReactive = texOutputReactive;
                texTransparencyAndComposition = null;
            }
            else
            {

                texReactive = fsr2PassData.OptReactiveMaskTex;
                texTransparencyAndComposition = fsr2PassData.OptTransparencyAndCompositionTex;
            }

            CommandBuffer cb = CommandBufferPool.Get();

            // texColor could still be used as render target, which will cause set resource failed later 
            cb.SetRenderTarget(RenderTargetHandle.CameraTarget.id);

            if (!initialized_)
            {
                Initialize(cb, ref renderingData);
            }
            initialized_ = false;

            AMDFFX.FSR2Pass.UpdateTexture(cb, texColor, AMDFFX.TextureName.FSR2_COLOR);
            AMDFFX.FSR2Pass.UpdateTexture(cb, texDepth, AMDFFX.TextureName.FSR2_DEPTH);
            AMDFFX.FSR2Pass.UpdateTexture(cb, texMotionVector, AMDFFX.TextureName.FSR2_MOTION_VECTORS);
            if (texReactive != null)
                AMDFFX.FSR2Pass.UpdateTexture(cb, texReactive, AMDFFX.TextureName.FSR2_REACTIVE);
            if (texTransparencyAndComposition != null)
                AMDFFX.FSR2Pass.UpdateTexture(cb, texTransparencyAndComposition, AMDFFX.TextureName.FSR2_TRANSPARENT_AND_COMPOSITION);
            AMDFFX.FSR2Pass.UpdateTexture(cb, texOutput, AMDFFX.TextureName.FSR2_OUTPUT);

            AMDFFX.FSR2Pass.FSR2ExecuteParam exeParam = new AMDFFX.FSR2Pass.FSR2ExecuteParam()
            {
                jitterOffset = AMDFFX.FSR2Pass.jitterOffset,
                motionVectorScale = new float[2] { -1 * renderSizeWidth, 1 * renderSizeHeight },
                renderSize = new UInt32[2] { (UInt32)renderSizeWidth, (UInt32)renderSizeHeight },
                enableSharpening = fsr2PassData.EnableSharpening,
                sharpness = fsr2PassData.Sharpness,
                frameTimeDeltaInSec = Time.deltaTime,
                cameraNear = camera.nearClipPlane,
                cameraFar = camera.farClipPlane,
                cameraFov = camera.fieldOfView * Mathf.Deg2Rad
            };
            AMDFFX.FSR2Pass.Execute(cb, ref exeParam);

            renderingData.cameraData.fsr2Output = texOutput;

            context.ExecuteCommandBuffer(cb);
            cb.Release();
        }
        else
        {
            renderingData.cameraData.fsr2Output = null;
        }
    }

    Matrix4x4 jitterMat = Matrix4x4.identity;
    public void Setup(FSR2RenderPassData fsr2_pass_data, ref RenderingData renderingData)
    {
        fsr2PassData = fsr2_pass_data;
        ref var camera_data = ref renderingData.cameraData;
        if (fsr2PassData.FSR2Quality != FSR2Mode.Disabled)
        {
            var camera = camera_data.camera;
            uint renderSizeWidth = (uint)(camera_data.camera.pixelWidth * camera_data.renderScale);
            uint renderSizeHeight = (uint)(camera.pixelHeight * camera_data.renderScale);
            AMDFFX.FSR2Pass.UpdateJitterOffset(renderSizeWidth, renderSizeHeight, (UInt32)camera.pixelWidth);
            var jitterOffset = AMDFFX.FSR2Pass.jitterOffset;
            jitterMat.m03 = 2.0f * jitterOffset[0] / renderSizeWidth;
            jitterMat.m13 = -2.0f * jitterOffset[1] / renderSizeHeight;
            camera_data.jitterMatrix = jitterMat;
        }
        else
            camera_data.jitterMatrix = Matrix4x4.identity;
    }
}
