using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

// Types the layer rule must reject, in a namespace of their own that LayerRuleTests scans like Berth.Core. None of
// them ever runs: the rule reads metadata only.
#pragma warning disable CA1812, CA1822, CA1823, CA2201, IDE0051, CS0169, CS0649

namespace Berth.Tests.LayerFixtures.Core
{
    internal sealed class Bad
    {
        internal System.Windows.Window? W { get; set; }
    }

    internal sealed class BodyOnly
    {
        internal void Warn() => System.Windows.MessageBox.Show("never shown");
    }

    internal sealed class GenericArgument
    {
        internal List<System.Windows.Window> windows = [];
    }

    // A UI interface: ICommand would not do, since .NET keeps it in System.ObjectModel, part of the base runtime.
    internal sealed class Ambient : System.Windows.Markup.IQueryAmbient
    {
        public bool IsAmbientPropertyAvailable(string propertyName) => false;
    }

    internal sealed class Primitives
    {
        internal System.Windows.Forms.Padding Margin { get; set; }
    }

    internal sealed class ComImportUser
    {
        internal Berth.Tests.LayerFixtures.Outside.IComImported? com;
    }

    internal sealed class GeneratedComUser
    {
        internal Berth.Tests.LayerFixtures.Outside.IComGenerated? com;
    }

    internal sealed class OuterLayerUser
    {
        internal Berth.Tests.LayerFixtures.Outside.OuterLayer? outer;
    }

    internal sealed class Clean
    {
        internal Dictionary<string, int> Counts { get; } = [];

        internal string Write() => System.Text.Json.JsonSerializer.Serialize(Counts);
    }
}

namespace Berth.Tests.LayerFixtures.Outside
{
    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IComImported
    {
    }

    [GeneratedComInterface]
    [Guid("1C3A4D41-5B2E-4C55-9D4E-7F3F2C1B0A01")]
    internal partial interface IComGenerated
    {
    }

    internal sealed class OuterLayer
    {
    }
}
