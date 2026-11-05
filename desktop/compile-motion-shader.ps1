$ErrorActionPreference='Stop'
if (-not ('CreditMonitorBuild.ShaderCompiler' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace CreditMonitorBuild {
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBlob {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] UIntPtr GetBufferSize();
    }
    public static class ShaderCompiler {
        [DllImport("d3dcompiler_47.dll", CallingConvention=CallingConvention.StdCall)]
        static extern int D3DCompile(byte[] data, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string name,
            IntPtr defines, IntPtr include, [MarshalAs(UnmanagedType.LPStr)] string entry,
            [MarshalAs(UnmanagedType.LPStr)] string profile, uint flags, uint effectFlags,
            out IBlob code, out IBlob errors);
        public static void Compile(string source, string destination) {
            byte[] input=Encoding.UTF8.GetBytes(File.ReadAllText(source));
            IBlob code=null, errors=null;
            try {
                int hr=D3DCompile(input, (UIntPtr)input.Length, Path.GetFileName(source), IntPtr.Zero,
                    IntPtr.Zero, "main", "ps_2_0", 0, 0, out code, out errors);
                if(hr < 0) throw new InvalidOperationException(errors == null ? "Shader compilation failed." : Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
                byte[] result=new byte[checked((int)code.GetBufferSize().ToUInt64())];
                Marshal.Copy(code.GetBufferPointer(), result, 0, result.Length);
                File.WriteAllBytes(destination,result);
            } finally {
                if(code != null) Marshal.ReleaseComObject(code);
                if(errors != null) Marshal.ReleaseComObject(errors);
            }
        }
    }
}
'@
}
[CreditMonitorBuild.ShaderCompiler]::Compile((Join-Path $PSScriptRoot 'src/HorizontalMotionBlur.hlsl'), (Join-Path $PSScriptRoot 'src/HorizontalMotionBlur.ps'))
