using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class AMDFFX
{
    public enum TextureName
    {
        INVALID = 0,
        FSR2_COLOR = 1,
        FSR2_DEPTH,
        FSR2_MOTION_VECTORS,
        FSR2_REACTIVE,
        FSR2_TRANSPARENT_AND_COMPOSITION,
        FSR2_OUTPUT,
        FSR2_COLOR_OPAQUE_ONLY,
        FSR2_COLOR_PRE_UPSCALE,
        MAX
    }

    static uint EncodeTextureUpdateUserData(TextureName texName)
    {
        return (uint)texName;
    }

    const String fsr2_unity_plugin_name_ = "AMDUnityPlugin";

    [DllImport(fsr2_unity_plugin_name_)]
    static extern IntPtr AMD_GetCallbackTextureUpdate();

    [DllImport(fsr2_unity_plugin_name_)]
    static extern IntPtr AMD_FSR2_GetCallback();

    [DllImport(fsr2_unity_plugin_name_)]
    static extern void AMD_FSR2_GetJitterOffset(UInt32 index, UInt32 renderWidth, UInt32 renderHeight, UInt32 displayWidth, [MarshalAs(UnmanagedType.LPArray, SizeConst = 2)] float[] jitter_offset);


    public class FSR2Pass
    {
        public enum PassEvent
        {
            INVALID = 0,
            INITIALIZE = 1,
            EXECUTE,
            REACTIVEMASK,
            MAX
        };
        public struct FSR2InitParam
        {
            [System.Flags]
            public enum FlagBits
            {
                FFX_FSR2_ENABLE_HIGH_DYNAMIC_RANGE = (1 << 0),
                FFX_FSR2_ENABLE_DISPLAY_RESOLUTION_MOTION_VECTORS = (1 << 1),
                FFX_FSR2_ENABLE_MOTION_VECTORS_JITTER_CANCELLATION = (1 << 2),
                FFX_FSR2_ENABLE_DEPTH_INVERTED = (1 << 3),
                FFX_FSR2_ENABLE_DEPTH_INFINITE = (1 << 4),
                FFX_FSR2_ENABLE_AUTO_EXPOSURE = (1 << 5),
                FFX_FSR2_ENABLE_DYNAMIC_RESOLUTION = (1 << 6),
                FFX_FSR2_ENABLE_TEXTURE1D_USAGE = (1 << 7)
            }
            public UInt32 flags;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public UInt32[] displaySize;
        }
        private static FSR2InitParam initParam = new()
        {
            flags = (UInt32)(FSR2InitParam.FlagBits.FFX_FSR2_ENABLE_AUTO_EXPOSURE
            ),
            displaySize = new UInt32[2] { 0, 0 }
        };
        public struct FSR2GenReactiveParam
        {
            [System.Flags]
            public enum FlagBits
            {
                FFX_FSR2_AUTOREACTIVEFLAGS_NONE = 0,
                FFX_FSR2_AUTOREACTIVEFLAGS_APPLY_TONEMAP = (1 << 0),
                FFX_FSR2_AUTOREACTIVEFLAGS_APPLY_INVERSETONEMAP = (1 << 1),
                FFX_FSR2_AUTOREACTIVEFLAGS_APPLY_THRESHOLD = (1 << 2),
                FFX_FSR2_AUTOREACTIVEFLAGS_USE_COMPONENTS_MAX = (1 << 3)
            }
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public UInt32[] renderSize;
            public float scale;
            public float cutoffThreshold;
            public float binaryValue;
            public UInt32 flags;
        }
        public struct FSR2ExecuteParam
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public float[] jitterOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public float[] motionVectorScale;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public UInt32[] renderSize;
            [MarshalAs(UnmanagedType.I1)]
            public bool enableSharpening;
            public float sharpness;
            public float frameTimeDeltaInSec;
            public float cameraNear;
            public float cameraFar;
            public float cameraFov;
        }


        public static void UpdateTexture(CommandBuffer cb, Texture tex, TextureName texName)
        {
            if (tex != null && AMD_GetCallbackTextureUpdate() != IntPtr.Zero)
                cb.IssuePluginCustomTextureUpdateV2(AMD_GetCallbackTextureUpdate(), tex, EncodeTextureUpdateUserData(texName));
        }

        static IntPtr ptrExeParam = Marshal.AllocHGlobal(Marshal.SizeOf<FSR2ExecuteParam>());
        static IntPtr ptrInitParam = Marshal.AllocHGlobal(Marshal.SizeOf<FSR2InitParam>());
        static IntPtr ptrGenReactiveParam = Marshal.AllocHGlobal(Marshal.SizeOf<FSR2GenReactiveParam>());
        public static void Destory()
        {
            Marshal.DestroyStructure<FSR2ExecuteParam>(ptrExeParam);
            Marshal.DestroyStructure<FSR2ExecuteParam>(ptrInitParam);
            Marshal.DestroyStructure<FSR2ExecuteParam>(ptrGenReactiveParam);
        }

        static UInt32 index = 0;
        public static float[] jitterOffset = new float[2];
        public static void UpdateJitterOffset(UInt32 renderWidth, UInt32 renderHeight, UInt32 displayWidth)
        {
            AMD_FSR2_GetJitterOffset(index++, renderWidth, renderHeight, displayWidth, jitterOffset);
        }
        public static void Initialzie(CommandBuffer cb, Camera camera, UInt32 flags)
        {
            var displaySize = initParam.displaySize;
            var needToInit = false;
            if (displaySize[0] != camera.pixelWidth || displaySize[1] != camera.pixelHeight)
            {
                displaySize[0] = (UInt32)camera.pixelWidth;
                displaySize[1] = (UInt32)camera.pixelHeight;
                needToInit = true;
            }
            if (initParam.flags != flags)
            {
                initParam.flags = flags;
                needToInit = true;
            }
            if (needToInit)
            {
                Marshal.StructureToPtr(initParam, ptrInitParam, false);
                cb.IssuePluginEventAndData(AMD_FSR2_GetCallback(), (int)PassEvent.INITIALIZE, ptrInitParam);
            }
        }

        public static void Execute(CommandBuffer cb, ref FSR2ExecuteParam exeParam)
        {
            Marshal.StructureToPtr(exeParam, ptrExeParam, false);
            cb.IssuePluginEventAndData(AMD_FSR2_GetCallback(), (int)PassEvent.EXECUTE, ptrExeParam);

        }

        public static TextureHandle thFSR2ReactiveMask;
        public static void GenReactiveMask(CommandBuffer cb, ref FSR2GenReactiveParam genReactiveParam)
        {
            Marshal.StructureToPtr(genReactiveParam, ptrGenReactiveParam, false);
            cb.IssuePluginEventAndData(AMD_FSR2_GetCallback(), (int)PassEvent.REACTIVEMASK, ptrGenReactiveParam);
        }
    }
}
