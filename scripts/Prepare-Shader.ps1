$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LaterarootShaderCompiler {
    [DllImport("d3dcompiler_47.dll", CharSet=CharSet.Ansi)]
    public static extern int D3DCompile(byte[] source, UIntPtr length, string name, IntPtr defines, IntPtr include, string entry, string target, uint flags, uint flags2, out IntPtr code, out IntPtr errors);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate IntPtr Pointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate UIntPtr Size(IntPtr self);
    public static byte[] Read(IntPtr blob) {
        IntPtr table=Marshal.ReadIntPtr(blob);
        IntPtr data=Marshal.GetDelegateForFunctionPointer<Pointer>(Marshal.ReadIntPtr(table,3*IntPtr.Size))(blob);
        int length=checked((int)Marshal.GetDelegateForFunctionPointer<Size>(Marshal.ReadIntPtr(table,4*IntPtr.Size))(blob).ToUInt64());
        byte[] result=new byte[length]; Marshal.Copy(data,result,0,length); return result;
    }
}
'@
$sourcePath = Join-Path $PSScriptRoot '../src/Lateraroot/Assets/Glass.hlsl'
$source = [IO.File]::ReadAllBytes($sourcePath)
$code = [IntPtr]::Zero; $errors = [IntPtr]::Zero
try {
    $hr = [LaterarootShaderCompiler]::D3DCompile($source, [UIntPtr]$source.Length, 'Glass.hlsl', [IntPtr]::Zero, [IntPtr]::Zero, 'main', 'ps_3_0', 32768, 0, [ref]$code, [ref]$errors)
    if ($hr -lt 0) {
        $message = if ($errors -ne [IntPtr]::Zero) { [Text.Encoding]::UTF8.GetString([LaterarootShaderCompiler]::Read($errors)) } else { "HRESULT $hr" }
        throw $message
    }
    [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot '../src/Lateraroot/Assets/Glass.ps'), [LaterarootShaderCompiler]::Read($code))
    'Compiled Glass.hlsl to a packaged PS 3.0 shader; no compiler is required at runtime.'
} finally {
    if ($code -ne [IntPtr]::Zero) { [void][Runtime.InteropServices.Marshal]::Release($code) }
    if ($errors -ne [IntPtr]::Zero) { [void][Runtime.InteropServices.Marshal]::Release($errors) }
}
