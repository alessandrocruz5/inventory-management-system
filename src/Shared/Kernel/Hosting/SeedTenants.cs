namespace Kernel.Hosting;

/// <summary>The fixed tenant ids the seed uses, so a second run finds what the first created (spec §3).</summary>
public static class SeedTenants
{
    /// <summary>The retail demo tenant.</summary>
    public static readonly Guid Demo = new("00000000-0000-0000-0000-00000000d001");

    /// <summary>The second tenant, for isolation checks.</summary>
    public static readonly Guid Isolation = new("00000000-0000-0000-0000-00000000d002");
}