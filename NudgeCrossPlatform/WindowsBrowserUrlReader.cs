using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NudgeCore;

#if WINDOWS

// IUIAutomation — method order MUST match the COM vtable exactly (UIAutomationClient.h).
// We only invoke ElementFromHandle and CreatePropertyCondition, but every preceding slot
// must be declared so those two land on the correct function pointer.
[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"),
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
    [PreserveSig] int Get_ControlViewWalker([MarshalAs(UnmanagedType.Interface)] out object walker);
    [PreserveSig] int Get_ContentViewWalker([MarshalAs(UnmanagedType.Interface)] out object walker);
    [PreserveSig] int Get_RawViewWalker([MarshalAs(UnmanagedType.Interface)] out object walker);
    [PreserveSig] int Get_RawViewCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int Get_ControlViewCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int Get_ContentViewCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateCacheRequest([MarshalAs(UnmanagedType.Interface)] out object cr);
    [PreserveSig] int CreateTrueCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreateFalseCondition([MarshalAs(UnmanagedType.Interface)] out object cond);
    [PreserveSig] int CreatePropertyCondition(int pid, object val, [MarshalAs(UnmanagedType.Interface)] out object cond);
}

// IUIAutomationElement — method order MUST match the COM vtable exactly (UIAutomationClient.h).
// We invoke FindFirst and GetCurrentPattern; declarations stop at GetCurrentPattern since no
// later slot is used. Unused intermediate slots keep placeholder signatures (never called).
[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    [PreserveSig] int SetFocus();
    [PreserveSig] int GetRuntimeId(out object arr);
    [PreserveSig] int FindFirst(int scope, IntPtr cond, [MarshalAs(UnmanagedType.Interface)] out object found);
    [PreserveSig] int FindAll(int scope, IntPtr cond, out object found);
    [PreserveSig] int FindFirstBuildCache(int scope, IntPtr cond, IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object found);
    [PreserveSig] int FindAllBuildCache(int scope, IntPtr cond, IntPtr cr, out object found);
    [PreserveSig] int BuildUpdatedCache(IntPtr cr, [MarshalAs(UnmanagedType.Interface)] out object el);
    [PreserveSig] int GetCurrentPropertyValue(int pid, out object val);
    [PreserveSig] int GetCurrentPropertyValueEx(int pid, int ignDef, out object val);
    [PreserveSig] int GetCachedPropertyValue(int pid, out object val);
    [PreserveSig] int GetCachedPropertyValueEx(int pid, int ignDef, out object val);
    [PreserveSig] int GetCurrentPatternAs(int pid, ref Guid riid, out IntPtr pat);
    [PreserveSig] int GetCachedPatternAs(int pid, ref Guid riid, out IntPtr pat);
    [PreserveSig] int GetCurrentPattern(int pid, [MarshalAs(UnmanagedType.Interface)] out object pat);
}

[ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"),
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
