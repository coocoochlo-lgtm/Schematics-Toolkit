using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace SchematicsAgent;

/// <summary>What the models run on. Without AVX2, or with few cores, the standard language model is very slow.</summary>
public static class Cpu
{
    /// <summary>Too slow for the standard model: Unity switches to the fast one (when its choice is Auto).</summary>
    public static bool Slow => !Avx2.IsSupported || Environment.ProcessorCount <= 2;

    public static string Describe()
    {
        string name = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
        string total = Environment.GetEnvironmentVariable("NUMBER_OF_PROCESSORS") ?? "";
        // Fewer cores than the machine has means something (a job object, affinity, a VM) limits this process.
        string cores = total.Length > 0 && total != Environment.ProcessorCount.ToString()
            ? $"{Environment.ProcessorCount} of {total} cores" : $"{Environment.ProcessorCount} cores";
        string emulated = RuntimeInformation.OSArchitecture != RuntimeInformation.ProcessArchitecture
            ? $", {RuntimeInformation.ProcessArchitecture} under emulation on {RuntimeInformation.OSArchitecture}" : "";
        return $"{name}, {cores}{emulated}, AVX {(Avx.IsSupported ? "yes" : "no")}, AVX2 {(Avx2.IsSupported ? "yes" : "no")}";
    }
}
