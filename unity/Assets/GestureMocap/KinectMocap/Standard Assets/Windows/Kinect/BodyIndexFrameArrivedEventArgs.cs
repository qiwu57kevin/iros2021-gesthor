#if (UNITY_STANDALONE_WIN)
using RootSystem = System;
using System.Linq;
using System.Collections.Generic;
namespace Windows.Kinect
{
    //
    // Windows.Kinect.BodyIndexFrameArrivedEventArgs
    //
    public sealed partial class BodyIndexFrameArrivedEventArgs : RootSystem.EventArgs, Helper.INativeWrapper

    {
        internal RootSystem.IntPtr _pNative;
        RootSystem.IntPtr Helper.INativeWrapper.nativePtr { get { return _pNative; } }

        // Constructors and Finalizers
        internal BodyIndexFrameArrivedEventArgs(RootSystem.IntPtr pNative)
        {
            _pNative = pNative;
            Windows_Kinect_BodyIndexFrameArrivedEventArgs_AddRefObject(ref _pNative);
        }

        ~BodyIndexFrameArrivedEventArgs()
        {
            Dispose(false);
        }

        [RootSystem.Runtime.InteropServices.DllImport("KinectUnityAddin", CallingConvention=RootSystem.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError=true)]
        private static extern void Windows_Kinect_BodyIndexFrameArrivedEventArgs_ReleaseObject(ref RootSystem.IntPtr pNative);
        [RootSystem.Runtime.InteropServices.DllImport("KinectUnityAddin", CallingConvention=RootSystem.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError=true)]
        private static extern void Windows_Kinect_BodyIndexFrameArrivedEventArgs_AddRefObject(ref RootSystem.IntPtr pNative);
        private void Dispose(bool disposing)
        {
            if (_pNative == RootSystem.IntPtr.Zero)
            {
                return;
            }

            __EventCleanup();

            Helper.NativeObjectCache.RemoveObject<BodyIndexFrameArrivedEventArgs>(_pNative);
                Windows_Kinect_BodyIndexFrameArrivedEventArgs_ReleaseObject(ref _pNative);

            _pNative = RootSystem.IntPtr.Zero;
        }


        // Public Properties
        [RootSystem.Runtime.InteropServices.DllImport("KinectUnityAddin", CallingConvention=RootSystem.Runtime.InteropServices.CallingConvention.Cdecl, SetLastError=true)]
        private static extern RootSystem.IntPtr Windows_Kinect_BodyIndexFrameArrivedEventArgs_get_FrameReference(RootSystem.IntPtr pNative);
        public  Windows.Kinect.BodyIndexFrameReference FrameReference
        {
            get
            {
                if (_pNative == RootSystem.IntPtr.Zero)
                {
                    throw new RootSystem.ObjectDisposedException("BodyIndexFrameArrivedEventArgs");
                }

                RootSystem.IntPtr objectPointer = Windows_Kinect_BodyIndexFrameArrivedEventArgs_get_FrameReference(_pNative);
                Helper.ExceptionHelper.CheckLastError();
                if (objectPointer == RootSystem.IntPtr.Zero)
                {
                    return null;
                }

                return Helper.NativeObjectCache.CreateOrGetObject<Windows.Kinect.BodyIndexFrameReference>(objectPointer, n => new Windows.Kinect.BodyIndexFrameReference(n));
            }
        }

        private void __EventCleanup()
        {
        }
    }

}
#endif
