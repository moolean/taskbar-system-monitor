// build.ps1 invokes csc directly, so the SDK does not generate this attribute.
// Without it, CLR compatibility defaults can silently select legacy TLS even
// on a machine with .NET Framework 4.8 installed. Keep both build paths aligned.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
