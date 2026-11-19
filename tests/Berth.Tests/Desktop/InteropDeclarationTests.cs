using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Berth.Desktop.Interop;

namespace Berth.Tests.Desktop;

public sealed class InteropDeclarationTests
{
    private readonly record struct ParamSpec(
        Type Type,
        bool IsIn,
        bool IsOut,
        UnmanagedType? MarshalAs = null,
        short? SizeParamIndex = null);

    private readonly record struct MethodSpec(
        Type ComType,
        int SlotIndex,
        string Name,
        Type ReturnType,
        bool PreserveSig,
        UnmanagedType? ReturnMarshalAs,
        params ParamSpec[] Parameters)
    {
        public MethodSpec(
            Type ComType,
            int SlotIndex,
            string Name,
            Type ReturnType,
            bool PreserveSig,
            params ParamSpec[] Parameters)
            : this(ComType, SlotIndex, Name, ReturnType, PreserveSig, ReturnMarshalAs: null, Parameters)
        {
        }
    }

    public static TheoryData<Type, Guid, ComInterfaceType, int, string[]> Interfaces() => new()
    {
        {
            typeof(IShellWindows),
            new Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"),
            ComInterfaceType.InterfaceIsDual,
            7,
            [
                "get_Count",
                "Item",
                "_NewEnum",
                "Register",
                "RegisterPending",
                "Revoke",
                "OnNavigate",
                "OnActivated",
                "FindWindowSW",
                "OnCreated",
                "ProcessAttachDetach",
            ]
        },
        {
            typeof(Berth.Desktop.Interop.IServiceProvider),
            new Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),
            ComInterfaceType.InterfaceIsIUnknown,
            3,
            ["QueryService"]
        },
        {
            typeof(IShellBrowser),
            new Guid("000214E2-0000-0000-C000-000000000046"),
            ComInterfaceType.InterfaceIsIUnknown,
            3,
            [
                "GetWindow",
                "ContextSensitiveHelp",
                "InsertMenusSB",
                "SetMenuSB",
                "RemoveMenusSB",
                "SetStatusTextSB",
                "EnableModelessSB",
                "TranslateAcceleratorSB",
                "BrowseObject",
                "GetViewStateStream",
                "GetControlWindow",
                "SendControlMsg",
                "QueryActiveShellView",
                "OnViewWindowActive",
                "SetToolbarItems",
            ]
        },
        {
            typeof(IShellView),
            new Guid("000214E3-0000-0000-C000-000000000046"),
            ComInterfaceType.InterfaceIsIUnknown,
            3,
            [
                "GetWindow",
                "ContextSensitiveHelp",
                "TranslateAccelerator",
                "EnableModeless",
                "UIActivate",
                "Refresh",
                "CreateViewWindow",
                "DestroyViewWindow",
                "GetCurrentInfo",
                "AddPropertySheetPages",
                "SaveViewState",
                "SelectItem",
                "GetItemObject",
            ]
        },
        {
            typeof(IFolderView),
            new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"),
            ComInterfaceType.InterfaceIsIUnknown,
            3,
            [
                "GetCurrentViewMode",
                "SetCurrentViewMode",
                "GetFolder",
                "Item",
                "ItemCount",
                "Items",
                "GetSelectionMarkedItem",
                "GetFocusedItem",
                "GetItemPosition",
                "GetSpacing",
                "GetDefaultSpacing",
                "GetAutoArrange",
                "SelectItem",
                "SelectAndPositionItems",
            ]
        },
        {
            typeof(IFolderView2),
            new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"),
            ComInterfaceType.InterfaceIsIUnknown,
            3,
            [
                "GetCurrentViewMode",
                "SetCurrentViewMode",
                "GetFolder",
                "Item",
                "ItemCount",
                "Items",
                "GetSelectionMarkedItem",
                "GetFocusedItem",
                "GetItemPosition",
                "GetSpacing",
                "GetDefaultSpacing",
                "GetAutoArrange",
                "SelectItem",
                "SelectAndPositionItems",
                "SetGroupBy",
                "GetGroupBy",
                "SetViewProperty",
                "GetViewProperty",
                "SetTileViewProperties",
                "SetExtendedTileViewProperties",
                "SetText",
                "SetCurrentFolderFlags",
                "GetCurrentFolderFlags",
            ]
        },
        {
            typeof(IShellFolder),
            new Guid("000214E6-0000-0000-C000-000000000046"),
            ComInterfaceType.InterfaceIsIUnknown,
            3,
            [
                "ParseDisplayName",
                "EnumObjects",
                "BindToObject",
                "BindToStorage",
                "CompareIDs",
                "CreateViewObject",
                "GetAttributesOf",
                "GetUIObjectOf",
                "GetDisplayNameOf",
                "SetNameOf",
            ]
        },
        {
            typeof(IEnumIDList),
            new Guid("000214F2-0000-0000-C000-000000000046"),
            ComInterfaceType.InterfaceIsIUnknown,
            3,
            ["Next", "Skip", "Reset", "Clone"]
        },
    };

    [Theory]
    [MemberData(nameof(Interfaces))]
    public void InterfaceHasDocumentedGuidAttributeComImportFlattenedHierarchyAndVtableOrder(
        Type comType,
        Guid expectedGuid,
        ComInterfaceType expectedInterfaceType,
        int baseVtableSlots,
        string[] expectedMethods)
    {
        Assert.True(comType.IsInterface, $"{comType.Name} must be an interface.");
        Assert.True(comType.IsImport, $"{comType.Name} must carry [ComImport].");
        Assert.Empty(comType.GetInterfaces());

        GuidAttribute? guidAttr = comType.GetCustomAttribute<GuidAttribute>();
        Assert.NotNull(guidAttr);
        Assert.Equal(expectedGuid, Guid.Parse(guidAttr.Value));

        InterfaceTypeAttribute? ifaceAttr = comType.GetCustomAttribute<InterfaceTypeAttribute>();
        Assert.NotNull(ifaceAttr);
        Assert.Equal(expectedInterfaceType, ifaceAttr.Value);

        string[] declared = DeclaredMethods(comType).Select(m => m.Name).ToArray();
        Assert.Equal(expectedMethods, declared);
        Assert.True(baseVtableSlots is 3 or 7);
    }

    [Fact]
    public void KeyTargetMethodsLandAtExactDocumentedVtableSlots()
    {
        AssertSlot(typeof(IShellWindows), 7, 15, "FindWindowSW");
        AssertSlot(typeof(Berth.Desktop.Interop.IServiceProvider), 3, 3, "QueryService");
        AssertSlot(typeof(IShellBrowser), 3, 15, "QueryActiveShellView");
        AssertSlot(typeof(IFolderView), 3, 14, "GetAutoArrange");
        AssertSlot(typeof(IFolderView), 3, 16, "SelectAndPositionItems");
        AssertSlot(typeof(IFolderView2), 3, 16, "SelectAndPositionItems");
        AssertSlot(typeof(IFolderView2), 3, 24, "SetCurrentFolderFlags");
        AssertSlot(typeof(IFolderView2), 3, 25, "GetCurrentFolderFlags");
        AssertSlot(typeof(IShellFolder), 3, 11, "GetDisplayNameOf");
        AssertSlot(typeof(IEnumIDList), 3, 3, "Next");
    }

    public static TheoryData<object> CalledMethodSignatures()
    {
        var data = new TheoryData<object>();

        data.Add(new MethodSpec(
            typeof(IShellWindows),
            1,
            "Item",
            typeof(object),
            PreserveSig: false,
            UnmanagedType.IDispatch,
            new ParamSpec(typeof(object), IsIn: false, IsOut: false, UnmanagedType.Struct)));

        data.Add(new MethodSpec(
            typeof(IShellWindows),
            2,
            "_NewEnum",
            typeof(object),
            PreserveSig: false,
            UnmanagedType.IUnknown));

        data.Add(new MethodSpec(
            typeof(IShellWindows),
            8,
            "FindWindowSW",
            typeof(int),
            PreserveSig: true,
            new ParamSpec(typeof(object).MakeByRefType(), IsIn: true, IsOut: false, UnmanagedType.Struct),
            new ParamSpec(typeof(object).MakeByRefType(), IsIn: true, IsOut: false, UnmanagedType.Struct),
            new ParamSpec(typeof(int), IsIn: false, IsOut: false),
            new ParamSpec(typeof(int).MakeByRefType(), IsIn: false, IsOut: true),
            new ParamSpec(typeof(int), IsIn: false, IsOut: false),
            new ParamSpec(typeof(object).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.IDispatch)));

        data.Add(new MethodSpec(
            typeof(Berth.Desktop.Interop.IServiceProvider),
            0,
            "QueryService",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(Guid).MakeByRefType(), IsIn: true, IsOut: false),
            new ParamSpec(typeof(Guid).MakeByRefType(), IsIn: true, IsOut: false),
            new ParamSpec(typeof(object).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

        data.Add(new MethodSpec(
            typeof(IShellBrowser),
            12,
            "QueryActiveShellView",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(IShellView).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

        data.Add(new MethodSpec(
            typeof(IShellView),
            12,
            "GetItemObject",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(Guid).MakeByRefType(), IsIn: true, IsOut: false),
            new ParamSpec(typeof(object).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

        foreach (Type viewType in new[] { typeof(IFolderView), typeof(IFolderView2) })
        {
            data.Add(new MethodSpec(
                viewType,
                2,
                "GetFolder",
                typeof(void),
                PreserveSig: false,
                new ParamSpec(typeof(Guid).MakeByRefType(), IsIn: true, IsOut: false),
                new ParamSpec(typeof(object).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

            data.Add(new MethodSpec(
                viewType,
                3,
                "Item",
                typeof(void),
                PreserveSig: false,
                new ParamSpec(typeof(int), IsIn: false, IsOut: false),
                new ParamSpec(typeof(nint).MakeByRefType(), IsIn: false, IsOut: true)));

            data.Add(new MethodSpec(
                viewType,
                4,
                "ItemCount",
                typeof(void),
                PreserveSig: false,
                new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
                new ParamSpec(typeof(int).MakeByRefType(), IsIn: false, IsOut: true)));

            data.Add(new MethodSpec(
                viewType,
                5,
                "Items",
                typeof(void),
                PreserveSig: false,
                new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
                new ParamSpec(typeof(Guid).MakeByRefType(), IsIn: true, IsOut: false),
                new ParamSpec(typeof(object).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

            data.Add(new MethodSpec(
                viewType,
                8,
                "GetItemPosition",
                typeof(void),
                PreserveSig: false,
                new ParamSpec(typeof(nint), IsIn: false, IsOut: false),
                new ParamSpec(typeof(NativePoint).MakeByRefType(), IsIn: false, IsOut: true)));

            data.Add(new MethodSpec(
                viewType,
                11,
                "GetAutoArrange",
                typeof(int),
                PreserveSig: true));

            data.Add(new MethodSpec(
                viewType,
                13,
                "SelectAndPositionItems",
                typeof(void),
                PreserveSig: false,
                new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
                new ParamSpec(typeof(nint[]), IsIn: true, IsOut: false, UnmanagedType.LPArray, SizeParamIndex: 0),
                new ParamSpec(typeof(NativePoint[]), IsIn: true, IsOut: false, UnmanagedType.LPArray, SizeParamIndex: 0),
                new ParamSpec(typeof(uint), IsIn: false, IsOut: false)));
        }

        data.Add(new MethodSpec(
            typeof(IFolderView2),
            21,
            "SetCurrentFolderFlags",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false)));

        data.Add(new MethodSpec(
            typeof(IFolderView2),
            22,
            "GetCurrentFolderFlags",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(uint).MakeByRefType(), IsIn: false, IsOut: true)));

        data.Add(new MethodSpec(
            typeof(IShellFolder),
            1,
            "EnumObjects",
            typeof(int),
            PreserveSig: true,
            new ParamSpec(typeof(nint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(IEnumIDList).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

        data.Add(new MethodSpec(
            typeof(IShellFolder),
            4,
            "CompareIDs",
            typeof(int),
            PreserveSig: true,
            new ParamSpec(typeof(nint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(nint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(nint), IsIn: false, IsOut: false)));

        data.Add(new MethodSpec(
            typeof(IShellFolder),
            7,
            "GetUIObjectOf",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(nint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(nint[]), IsIn: true, IsOut: false, UnmanagedType.LPArray, SizeParamIndex: 1),
            new ParamSpec(typeof(Guid).MakeByRefType(), IsIn: true, IsOut: false),
            new ParamSpec(typeof(nint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(object).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

        data.Add(new MethodSpec(
            typeof(IShellFolder),
            8,
            "GetDisplayNameOf",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(nint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(StrRet).MakeByRefType(), IsIn: false, IsOut: true)));

        data.Add(new MethodSpec(
            typeof(IEnumIDList),
            0,
            "Next",
            typeof(int),
            PreserveSig: true,
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false),
            new ParamSpec(typeof(nint[]), IsIn: false, IsOut: true, UnmanagedType.LPArray, SizeParamIndex: 0),
            new ParamSpec(typeof(uint).MakeByRefType(), IsIn: false, IsOut: true)));

        data.Add(new MethodSpec(
            typeof(IEnumIDList),
            1,
            "Skip",
            typeof(int),
            PreserveSig: true,
            new ParamSpec(typeof(uint), IsIn: false, IsOut: false)));

        data.Add(new MethodSpec(
            typeof(IEnumIDList),
            2,
            "Reset",
            typeof(void),
            PreserveSig: false));

        data.Add(new MethodSpec(
            typeof(IEnumIDList),
            3,
            "Clone",
            typeof(void),
            PreserveSig: false,
            new ParamSpec(typeof(IEnumIDList).MakeByRefType(), IsIn: false, IsOut: true, UnmanagedType.Interface)));

        return data;
    }

    [Fact]
    public void StatusBearingOutParametersAreMarkedNullable()
    {
        var nullability = new NullabilityInfoContext();

        ParameterInfo findWindowDisp = DeclaredMethods(typeof(IShellWindows))[8].GetParameters()[5];
        Assert.Equal("FindWindowSW", findWindowDisp.Member.Name);
        Assert.Equal("ppdispOut", findWindowDisp.Name);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(findWindowDisp).WriteState);

        ParameterInfo enumOut = DeclaredMethods(typeof(IShellFolder))[1].GetParameters()[2];
        Assert.Equal("EnumObjects", enumOut.Member.Name);
        Assert.Equal("ppenumIDList", enumOut.Name);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(enumOut).WriteState);
    }

    [Fact]
    public void IFolderView2FlattensAllFourteenIFolderViewMethodsVerbatim()
    {
        MethodInfo[] baseMethods = DeclaredMethods(typeof(IFolderView));
        MethodInfo[] derivedMethods = DeclaredMethods(typeof(IFolderView2));

        Assert.Equal(14, baseMethods.Length);
        Assert.True(derivedMethods.Length >= baseMethods.Length);

        for (int i = 0; i < baseMethods.Length; i++)
        {
            MethodInfo expected = baseMethods[i];
            MethodInfo actualMethod = derivedMethods[i];

            Assert.Equal(expected.Name, actualMethod.Name);
            Assert.Equal(expected.ReturnType, actualMethod.ReturnType);
            Assert.Equal(
                (expected.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0,
                (actualMethod.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0);
            Assert.Equal(
                expected.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()?.Value,
                actualMethod.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()?.Value);

            ParameterInfo[] expParams = expected.GetParameters();
            ParameterInfo[] actParams = actualMethod.GetParameters();
            Assert.Equal(expParams.Length, actParams.Length);

            for (int j = 0; j < expParams.Length; j++)
            {
                Assert.Equal(expParams[j].ParameterType, actParams[j].ParameterType);
                Assert.Equal(expParams[j].IsIn, actParams[j].IsIn);
                Assert.Equal(expParams[j].IsOut, actParams[j].IsOut);

                UnmanagedType? expMarshal = expParams[j].GetCustomAttribute<MarshalAsAttribute>()?.Value;
                UnmanagedType? actMarshal = actParams[j].GetCustomAttribute<MarshalAsAttribute>()?.Value;
                Assert.Equal(expMarshal, actMarshal);
                if (expMarshal == UnmanagedType.LPArray)
                {
                    Assert.Equal(
                        ReadExplicitLpArraySizeParamIndex(expParams[j]),
                        ReadExplicitLpArraySizeParamIndex(actParams[j]));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(CalledMethodSignatures))]
    public void CalledMethodMatchesSdkSignatureAndMarshalling(object rawSpec)
    {
        var spec = (MethodSpec)rawSpec;
        MethodInfo method = DeclaredMethods(spec.ComType)[spec.SlotIndex];

        Assert.Equal(spec.Name, method.Name);
        Assert.Equal(spec.ReturnType, method.ReturnType);
        bool hasPreserveSig = (method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0;
        Assert.Equal(spec.PreserveSig, hasPreserveSig);

        MarshalAsAttribute? returnMarshal = method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>();
        if (spec.ReturnMarshalAs is null)
        {
            Assert.Null(returnMarshal);
        }
        else
        {
            Assert.NotNull(returnMarshal);
            Assert.Equal(spec.ReturnMarshalAs.Value, returnMarshal.Value);
        }

        ParameterInfo[] actual = method.GetParameters();
        Assert.Equal(spec.Parameters.Length, actual.Length);
        for (int i = 0; i < spec.Parameters.Length; i++)
        {
            ParamSpec exp = spec.Parameters[i];
            ParameterInfo got = actual[i];
            Assert.Equal(exp.Type, got.ParameterType);
            Assert.Equal(exp.IsIn, got.IsIn);
            Assert.Equal(exp.IsOut, got.IsOut);

            MarshalAsAttribute? marshal = got.GetCustomAttribute<MarshalAsAttribute>();
            if (exp.MarshalAs is null)
            {
                Assert.Null(marshal);
            }
            else
            {
                Assert.NotNull(marshal);
                Assert.Equal(exp.MarshalAs.Value, marshal.Value);
                if (exp.MarshalAs == UnmanagedType.LPArray)
                {
                    Assert.Equal(exp.SizeParamIndex, ReadExplicitLpArraySizeParamIndex(got));
                }
                else if (exp.SizeParamIndex is not null)
                {
                    Assert.Equal(exp.SizeParamIndex.Value, marshal.SizeParamIndex);
                }
            }
        }
    }

    private static short? ReadExplicitLpArraySizeParamIndex(ParameterInfo parameter)
    {
        byte[] bytes = File.ReadAllBytes(parameter.Member.Module.Assembly.Location);
        using var peReader = new PEReader(ImmutableArray.Create(bytes));
        MetadataReader mr = peReader.GetMetadataReader();
        Parameter paramDef = mr.GetParameter(MetadataTokens.ParameterHandle(parameter.MetadataToken));
        BlobHandle descriptor = paramDef.GetMarshallingDescriptor();
        if (descriptor.IsNil)
        {
            return null;
        }

        BlobReader reader = mr.GetBlobReader(descriptor);
        int nativeType = reader.ReadCompressedInteger();
        Assert.Equal(0x2A, nativeType);
        if (reader.RemainingBytes == 0)
        {
            return null;
        }

        _ = reader.ReadCompressedInteger();
        if (reader.RemainingBytes == 0)
        {
            return null;
        }

        int paramNum = reader.ReadCompressedInteger();
        if (reader.RemainingBytes == 0)
        {
            return (short)paramNum;
        }

        _ = reader.ReadCompressedInteger();
        if (reader.RemainingBytes == 0)
        {
            return (short)paramNum;
        }

        int flags = reader.ReadCompressedInteger();
        return (flags & 1) != 0 ? (short)paramNum : null;
    }

    [Fact]
    public void NativePointHasSigned32BitCoordinatesInSdkOrder()
    {
        Assert.Equal(8, Marshal.SizeOf<NativePoint>());
        Assert.Equal(typeof(int), typeof(NativePoint).GetField(nameof(NativePoint.X))!.FieldType);
        Assert.Equal(typeof(int), typeof(NativePoint).GetField(nameof(NativePoint.Y))!.FieldType);
        Assert.Equal(0, Marshal.OffsetOf<NativePoint>(nameof(NativePoint.X)));
        Assert.Equal(4, Marshal.OffsetOf<NativePoint>(nameof(NativePoint.Y)));

        var point = new NativePoint { X = 0x11223344, Y = -5 };
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativePoint>());
        try
        {
            Marshal.StructureToPtr(point, buffer, fDeleteOld: false);
            Assert.Equal(0x11223344, Marshal.ReadInt32(buffer, 0));
            Assert.Equal(-5, Marshal.ReadInt32(buffer, 4));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void StrRetMatchesWin64Pshpack8LayoutAndInlineAnsiStorage()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(260, Marshal.SizeOf<StrRetBuffer>());
        Assert.Equal(272, Marshal.SizeOf<StrRet>());

        Assert.Equal(typeof(uint), typeof(StrRet).GetField(nameof(StrRet.uType))!.FieldType);
        Assert.Equal(typeof(nint), typeof(StrRet).GetField(nameof(StrRet.pOleStr))!.FieldType);
        Assert.Equal(typeof(uint), typeof(StrRet).GetField(nameof(StrRet.uOffset))!.FieldType);
        Assert.Equal(typeof(StrRetBuffer), typeof(StrRet).GetField(nameof(StrRet.cStr))!.FieldType);

        Assert.Equal(0, Marshal.OffsetOf<StrRet>(nameof(StrRet.uType)));
        Assert.Equal(8, Marshal.OffsetOf<StrRet>(nameof(StrRet.pOleStr)));
        Assert.Equal(8, Marshal.OffsetOf<StrRet>(nameof(StrRet.uOffset)));
        Assert.Equal(8, Marshal.OffsetOf<StrRet>(nameof(StrRet.cStr)));

        var strRet = new StrRet { uType = 2u };
        strRet.cStr[0] = (byte)'A';
        strRet.cStr[259] = (byte)'Z';

        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<StrRet>());
        try
        {
            Marshal.StructureToPtr(strRet, buffer, fDeleteOld: false);
            Assert.Equal(2, Marshal.ReadInt32(buffer, 0));
            Assert.Equal((byte)'A', Marshal.ReadByte(buffer, 8));
            Assert.Equal((byte)'Z', Marshal.ReadByte(buffer, 267));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void ShellConstantsMatchDocumentedSdkValues()
    {
        Assert.Equal(0, ShellConstants.S_OK);
        Assert.Equal(1, ShellConstants.S_FALSE);
        Assert.Equal(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), ShellConstants.CLSID_ShellWindows);
        Assert.Equal(new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), ShellConstants.SID_STopLevelBrowser);
        Assert.Equal(0x0000, ShellConstants.CSIDL_DESKTOP);
        Assert.Equal(0x00000008, ShellConstants.SWC_DESKTOP);
        Assert.Equal(0x00000001, ShellConstants.SWFO_NEEDDISPATCH);
        Assert.Equal(0x00000001u, ShellConstants.SVGIO_SELECTION);
        Assert.Equal(0x00000002u, ShellConstants.SVGIO_ALLVIEW);
        Assert.Equal(0x00000080u, ShellConstants.SVSI_POSITIONITEM);
        Assert.Equal(0x0000u, ShellConstants.SHGDN_NORMAL);
        Assert.Equal(0x8000u, ShellConstants.SHGDN_FORPARSING);
        Assert.Equal(0x00000001u, ShellConstants.FWF_AUTOARRANGE);
    }

    private static MethodInfo[] DeclaredMethods(Type comType) =>
        comType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(m => m.MetadataToken)
            .ToArray();

    private static void AssertSlot(Type comType, int baseSlots, int expectedVtableSlot, string expectedMethodName)
    {
        int index = expectedVtableSlot - baseSlots;
        MethodInfo[] methods = DeclaredMethods(comType);
        Assert.InRange(index, 0, methods.Length - 1);
        Assert.Equal(expectedMethodName, methods[index].Name);
    }
}
