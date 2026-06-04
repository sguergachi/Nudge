using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NudgeCore;

#if WINDOWS

[ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    [PreserveSig] int CompareElements(IntPtr el1, IntPtr el2, out int areSame);
    [PreserveSig] int CompareRuntimeIds(IntPtr r1, IntPtr r2, out int areSame);
    [PreserveSig] int GetRootElement([MarshalAs(UnmanagedType.Interface)] out object root);
    [PreserveSig] int ElementFromHandle(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] out object element);
    [PreserveSig] int ElementFromPoint(int x, int y, [MarshalAs(UnmanagedType.Interface)] out object element);
    [PreserveSig] int GetFocusedElement([MarshalAs(UnmanagedType.Interface)] out object element);
    [PreserveSig] int GetRootElementBuildCache(IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object el);
    [PreserveSig] int ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object el);
    [PreserveSig] int ElementFromPointBuildCache(int x, int y, IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object el);
    [PreserveSig] int GetFocusedElementBuildCache(IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object el);
    [PreserveSig] int CreateTreeWalker(IntPtr cond, [MarshalAs(UnmanagedType.Interface)] out object walker);
    [PreserveSig] int Get_ControlViewCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int Get_ContentViewCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int Get_RawViewCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateTrueCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateFalseCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreatePropertyCondition(int pid, object val, [MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreatePropertyConditionEx(int pid, object val, int flags, [MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateAndCondition(IntPtr c1, IntPtr c2, [MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateAndConditionFromArray(object[] conds, [MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateOrCondition(IntPtr c1, IntPtr c2, [MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateOrConditionFromArray(object[] conds, [MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateNotCondition(IntPtr c, [MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int AddAutomationEventHandler(int eid, IntPtr el, int scope, IntPtr cr, IntPtr handler);
    [PreserveSig] int RemoveAutomationEventHandler(int eid, IntPtr el, IntPtr handler);
    [PreserveSig] int AddPropertyChangedEventHandlerNativeArray(IntPtr el, int scope, IntPtr cr, IntPtr handler, int[] pids, int count);
    [PreserveSig] int RemovePropertyChangedEventHandler(IntPtr el, IntPtr handler);
    [PreserveSig] int AddStructureChangedEventHandler(IntPtr el, int scope, IntPtr handler);
    [PreserveSig] int RemoveStructureChangedEventHandler(IntPtr el, IntPtr handler);
    [PreserveSig] int AddFocusChangedEventHandler(IntPtr cr, IntPtr handler);
    [PreserveSig] int RemoveFocusChangedEventHandler(IntPtr handler);
    [PreserveSig] int RemoveAllEventHandlers();
    [PreserveSig] int IntNativeArrayToSafeVariant(IntPtr arr, int count, out object safe);
    [PreserveSig] int IntSafeVariantToNativeArray(object safe, out IntPtr arr, out int count);
    [PreserveSig] int CreateCacheRequest([MarshalAs(UnmanagedType.Interface)] out object cr);
    [PreserveSig] int CreateTreeWalkerEx(IntPtr cond, IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object walker);
    [PreserveSig] int Unused38();
    [PreserveSig] int Unused39();
    [PreserveSig] int Unused40();
    [PreserveSig] int Unused41();
    [PreserveSig] int Unused42();
    [PreserveSig] int Unused43();
    [PreserveSig] int Unused44();
    [PreserveSig] int Unused45();
    [PreserveSig] int Unused46();
    [PreserveSig] int Unused47();
    [PreserveSig] int Unused48();
    [PreserveSig] int Unused49();
    [PreserveSig] int Unused50();
    [PreserveSig] int Unused51();
    [PreserveSig] int Unused52();
    [PreserveSig] int Unused53();
    [PreserveSig] int Unused54();
    [PreserveSig] int Unused55();
    [PreserveSig] int Unused56();
    [PreserveSig] int Unused57();
    [PreserveSig] int Unused58();
    [PreserveSig] int Unused59();
    [PreserveSig] int Unused60();
    [PreserveSig] int Unused61();
    [PreserveSig] int Unused62();
    [PreserveSig] int Unused63();
    [PreserveSig] int Unused64();
    [PreserveSig] int Unused65();
    [PreserveSig] int Unused66();
    [PreserveSig] int Unused67();
    [PreserveSig] int Unused68();
    [PreserveSig] int Unused69();
    [PreserveSig] int Unused70();
    [PreserveSig] int Unused71();
    [PreserveSig] int Unused72();
    [PreserveSig] int Unused73();
    [PreserveSig] int Unused74();
    [PreserveSig] int Unused75();
    [PreserveSig] int Unused76();
}

[ComImport, Guid("D6EAFED2-8A35-4F76-B8F0-7A10C9C1F4C3"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    [PreserveSig] int SetFocus();
    [PreserveSig] int GetRuntimeId(out object arr);
    [PreserveSig] int GetProcessId(out int pid);
    [PreserveSig] int GetControlType(out int ct);
    [PreserveSig] int GetLocalizedControlType(out string s);
    [PreserveSig] int GetName(out string name);
    [PreserveSig] int GetAcceleratorKey(out string s);
    [PreserveSig] int GetAccessKey(out string s);
    [PreserveSig] int GetHasKeyboardFocus(out int b);
    [PreserveSig] int GetIsKeyboardFocusable(out int b);
    [PreserveSig] int GetIsEnabled(out int b);
    [PreserveSig] int GetIsPassword(out int b);
    [PreserveSig] int GetAutomationId(out string id);
    [PreserveSig] int GetClassName(out string cn);
    [PreserveSig] int GetHelpText(out string s);
    [PreserveSig] int GetCulture(out int c);
    [PreserveSig] int GetIsControlElement(out int b);
    [PreserveSig] int GetIsContentElement(out int b);
    [PreserveSig] int GetLabeledBy(out object el);
    [PreserveSig] int GetNativeWindowHandle(out IntPtr hwnd);
    [PreserveSig] int GetItemType(out string s);
    [PreserveSig] int GetIsOffscreen(out int b);
    [PreserveSig] int GetOrientation(out int o);
    [PreserveSig] int GetFrameworkId(out string s);
    [PreserveSig] int GetIsRequiredForForm(out int b);
    [PreserveSig] int GetItemStatus(out string s);
    [PreserveSig] int GetBoundingRectangle(out double l, out double t, out double w, out double h);
    [PreserveSig] int GetControllerFor(out object arr);
    [PreserveSig] int GetDescribedBy(out object arr);
    [PreserveSig] int GetFlowsTo(out object arr);
    [PreserveSig] int GetProviderDescription(out string s);
    [PreserveSig] int FindFirst(int scope, IntPtr cond, [MarshalAs(UnmanagedType.Interface)] out object found);
    [PreserveSig] int FindAll(int scope, IntPtr cond, out object found);
    [PreserveSig] int FindFirstBuildCache(int scope, IntPtr cond, IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object found);
    [PreserveSig] int FindAllBuildCache(int scope, IntPtr cond, IntPtr cr, out object found);
    [PreserveSig] int BuildUpdatedCache(IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object el);
    [PreserveSig] int GetCurrentPropertyValue(int pid, out object val);
    [PreserveSig] int GetCurrentPropertyValueEx(int pid, int ignDef, out object val);
    [PreserveSig] int GetCachedPropertyValue(int pid, out object val);
    [PreserveSig] int GetCachedPropertyValueEx(int pid, int ignDef, out object val);
    [PreserveSig] int GetCurrentPatternAs(int pid, ref Guid riid, out object pat);
    [PreserveSig] int GetCachedPatternAs(int pid, ref Guid riid, out object pat);
    [PreserveSig] int GetCurrentPattern(int pid, [MarshalAs(UnmanagedType.Interface)] out object pat);
    [PreserveSig] int GetCachedPattern(int pid, [MarshalAs(UnmanagedType.Interface)] out object pat);
    [PreserveSig] int GetCachedParent([MarshalAs(UnmanagedType.Interface)] out object el);
    [PreserveSig] int GetCachedChildren(out object arr);
    [PreserveSig] int GetClickablePoint(out int x, out int y, out int got);
    [PreserveSig] int GetAriaRole(out string s);
    [PreserveSig] int GetAriaProperties(out string s);
    [PreserveSig] int GetIsDataValidForForm(out int b);
    [PreserveSig] int GetFullDescription(out string s);
    [PreserveSig] int GetAnnotationTypes(out object arr);
    [PreserveSig] int GetAnnotations(out object arr);
    [PreserveSig] int GetOptimizeForVisualContent(out int b);
    [PreserveSig] int GetLiveSetting(out int s);
    [PreserveSig] int GetIsPeripheral(out int b);
    [PreserveSig] int GetPositionInSet(out int p);
    [PreserveSig] int GetSizeOfSet(out int s);
    [PreserveSig] int GetLevel(out int l);
    [PreserveSig] int GetLocalizedLandmarkType(out string s);
    [PreserveSig] int GetLandmarkType(out int t);
    [PreserveSig] int GetHeadingLevel(out int l);
    [PreserveSig] int GetIsDialog(out int b);
    [PreserveSig] int GetMetadataValue(int tid, int mid, out object val);
    [PreserveSig] int GetLocalizedControlTypeEx(out string s);
    [PreserveSig] int GetFullDescriptionEx(out string s);
    [PreserveSig] int Unused90();
    [PreserveSig] int Unused91();
    [PreserveSig] int Unused92();
    [PreserveSig] int Unused93();
    [PreserveSig] int Unused94();
    [PreserveSig] int Unused95();
    [PreserveSig] int Unused96();
    [PreserveSig] int Unused97();
    [PreserveSig] int Unused98();
    [PreserveSig] int Unused99();
    [PreserveSig] int Unused100();
    [PreserveSig] int Unused101();
    [PreserveSig] int Unused102();
    [PreserveSig] int Unused103();
    [PreserveSig] int Unused104();
    [PreserveSig] int Unused105();
    [PreserveSig] int Unused106();
    [PreserveSig] int Unused107();
    [PreserveSig] int Unused108();
    [PreserveSig] int Unused109();
    [PreserveSig] int Unused110();
    [PreserveSig] int Unused111();
    [PreserveSig] int Unused112();
    [PreserveSig] int Unused113();
    [PreserveSig] int Unused114();
    [PreserveSig] int Unused115();
    [PreserveSig] int Unused116();
    [PreserveSig] int Unused117();
    [PreserveSig] int Unused118();
}

[ComImport, Guid("A9A5579E-98C9-48DF-A7A1-B0B3B1E9C8F1"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationValuePattern
{
    [PreserveSig] int SetValue([MarshalAs(UnmanagedType.LPWStr)] string val);
    [PreserveSig] int Get_CurrentValue([MarshalAs(UnmanagedType.BStr)] out string val);
    [PreserveSig] int Get_CurrentIsReadOnly(out int ro);
    [PreserveSig] int Get_CachedValue([MarshalAs(UnmanagedType.BStr)] out string val);
    [PreserveSig] int Get_CachedIsReadOnly(out int ro);
}

[ComImport, Guid("E22AD333-B25F-460C-83D0-0581107395C9")]
internal class CUIAutomation { }

internal static class WindowsBrowserUrlReader
{
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_EditControlTypeId = 50004;
    private const int UIA_ValuePatternId = 10002;
    private const int TreeScope_Descendants = 4;

    private static string? _cachedUrl;
    private static IntPtr _cachedHwnd;
    private static DateTime _urlCacheExpiry = DateTime.MinValue;
    private static IUIAutomation? _uia;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, char[] text, int count);

    internal static string? TryReadUrl()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            var now = DateTime.Now;
            if (hwnd == _cachedHwnd && now < _urlCacheExpiry)
                return _cachedUrl;

            string? url = TryReadUrlViaUia(hwnd);
            _cachedUrl = url;
            _cachedHwnd = hwnd;
            _urlCacheExpiry = now.AddMilliseconds(500);
            return url;
        }
        catch { return null; }
    }

    private static string? TryReadUrlViaUia(IntPtr hwnd)
    {
        _uia ??= (IUIAutomation)new CUIAutomation();

        // Get the UIA element for the specific browser window (not the desktop root)
        _uia.ElementFromHandle(hwnd, out object? windowObj);
        if (windowObj == null) return null;
        var window = (IUIAutomationElement)windowObj;

        // Create condition: ControlType = Edit (the address bar is an edit control)
        _uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_EditControlTypeId, out object? editCond);
        if (editCond == null) return null;

        var condPtr = Marshal.GetIUnknownForObject(editCond);
        try
        {
            // Find the first edit control inside the browser window
            int hr = window.FindFirst(TreeScope_Descendants, condPtr, out object? addrBarObj);
            if (hr != 0 || addrBarObj == null) return null;

            var addrBar = (IUIAutomationElement)addrBarObj;

            // Read the URL via ValuePattern
            hr = addrBar.GetCurrentPattern(UIA_ValuePatternId, out object? vpObj);
            if (hr != 0 || vpObj == null) return null;

            var vp = (IUIAutomationValuePattern)vpObj;
            hr = vp.Get_CurrentValue(out string? value);
            if (hr != 0 || string.IsNullOrEmpty(value)) return null;

            value = value.Trim();
            if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return value;

            if (value.Contains('.'))
                return "https://" + value;

            return null;
        }
        finally
        {
            Marshal.Release(condPtr);
        }
    }
}

#endif
