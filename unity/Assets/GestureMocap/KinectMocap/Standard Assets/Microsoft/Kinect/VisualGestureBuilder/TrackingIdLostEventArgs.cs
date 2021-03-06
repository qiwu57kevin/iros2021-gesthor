#if (UNITY_STANDALONE_WIN)
using RootSystem = System;
using System.Linq;
using System.Collections.Generic;
namespace Microsoft.Kinect.VisualGestureBuilder
{
    //
    // Microsoft.Kinect.VisualGestureBuilder.TrackingIdLostEventArgs
    //
    public sealed partial class TrackingIdLostEventArgs : RootSystem.EventArgs, Helper.INativeWrapper

    {
        internal RootSystem.IntPtr _pNative;
        RootSystem.IntPtr Helper.INativeWrapper.nativePtr { get { return _pNative; } }

        // Constructors and Finalizers
        internal TrackingIdLostEventArgs(RootSystem.IntPtr pNative)
        {
            _pNative = pNative;
            Microsoft_Kinect_VisualGestureBuilder_TrackingIdLostEventArgs_AddRefObject(ref _pNative);
        }

        ~TrackingIdLostEventArgs()
        {
            Dispose(false);
        }

        [RootSystem.Runtime.InteropServices.DllImport("KinectVisualGestureBuilderUnityAddin", CallingConvention=RootSystem.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError=true)]
        private static extern void Microsoft_Kinect_VisualGestureBuilder_TrackingIdLostEventArgs_ReleaseObject(ref RootSystem.IntPtr pNative);
        [RootSystem.Runtime.InteropServices.DllImport("KinectVisualGestureBuilderUnityAddin", CallingConvention=RootSystem.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError=true)]
        private static extern void Microsoft_Kinect_VisualGestureBuilder_TrackingIdLostEventArgs_AddRefObject(ref RootSystem.IntPtr pNative);
        private void Dispose(bool disposing)
        {
            if (_pNative == RootSystem.IntPtr.Zero)
            {
                return;
            }

            __EventCleanup();

            Helper.NativeObjectCache.RemoveObject<TrackingIdLostEventArgs>(_pNative);
                Microsoft_Kinect_VisualGestureBuilder_TrackingIdLostEventArgs_ReleaseObject(ref _pNative);

            _pNative = RootSystem.IntPtr.Zero;
        }


        // Public Properties
        [RootSystem.Runtime.InteropServices.DllImport("KinectVisualGestureBuilderUnityAddin", CallingConvention=RootSystem.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError=true)]
        private static extern ulong Microsoft_Kinect_VisualGestureBuilder_TrackingIdLostEventArgs_get_TrackingId(RootSystem.IntPtr pNative);
        public  ulong TrackingId
        {
            get
            {
                if (_pNative == RootSystem.IntPtr.Zero)
                {
                    throw new RootSystem.ObjectDisposedException("TrackingIdLostEventArgs");
                }

                return Microsoft_Kinect_VisualGestureBuilder_TrackingIdLostEventArgs_get_TrackingId(_pNative);
            }
        }

        private void __EventCleanup()
        {
        }
    }

}
#endif
