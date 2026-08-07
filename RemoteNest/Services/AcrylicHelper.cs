using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RemoteNest.Services;

/// <summary>
/// Gives app windows the Windows 11 Fluent acrylic backdrop with a user-adjustable
/// intensity.
///
/// The effect is drawn by DWM itself via <c>DWMWA_SYSTEMBACKDROP_TYPE</c>: it blurs and
/// desaturates whatever is behind the window and tints the result with the light/dark
/// surface color. That tint is why the app keeps its own identity instead of taking on the
/// wallpaper's color — the window still reads as a light (or dark) surface, just a frosted
/// one. Simply making the window see-through (the legacy blur-behind accent) does the
/// opposite: the desktop's colors bleed straight through and the UI turns whatever color
/// the wallpaper is. That is deliberately not used here.
///
/// Three rules make the backdrop actually appear, and breaking any one of them silently
/// yields an opaque or glass-looking window:
///   1. Nothing may paint an opaque surface over it — the WPF composition target and the
///      Window background have to be see-through. ModernWpf's window style only sets
///      Background via a style setter, so the direct (local) assignment below wins;
///      shadowing theme brushes at app level is unnecessary and leaks into every other
///      control that consumes them.
///   2. No accent policy may be set. A legacy accent (blur/acrylic-behind) takes priority
///      over the system backdrop and disables it.
///   3. ModernWpf's title bar must stay opaque, so DWM's own caption buttons — which it
///      keeps painting underneath — do not show through beside ModernWpf's.
///
/// <see cref="Level"/> then scales a tint the app paints over the backdrop, in the current
/// theme's surface color: level 1 is nearly solid (a hint of frost), level 100 is the pure
/// acrylic surface. Level 0 turns the backdrop off entirely for the classic opaque look.
/// </summary>
public static class AcrylicHelper
{
    private const string SettingsKey = "acrylicLevel";

    /// <summary>Overlay alpha at level 1 — the app surface with only a hint of frost.</summary>
    private const int MaxOverlayAlpha = 216;

    private static readonly List<Window> Tracked = new();

    /// <summary>0 = off (opaque, classic look), 1–100 = increasingly frosted.</summary>
    public static int Level { get; private set; }

    public static void Initialize()
    {
        var raw = SettingsStore.Get(SettingsKey);
        Level = int.TryParse(raw, out var saved) ? Math.Clamp(saved, 0, 100) : 0;
        UpdateSharedBrushes();
    }

    public static void SetLevel(int level, bool persist = true)
    {
        Level = Math.Clamp(level, 0, 100);
        if (persist) SettingsStore.Set(SettingsKey, Level.ToString());
        ReapplyAll();
    }

    /// <summary>Registers a window so it gets the backdrop now and after every later change.</summary>
    public static void Track(Window window)
    {
        if (Tracked.Contains(window)) return;
        Tracked.Add(window);
        window.Closed += (_, _) => Tracked.Remove(window);

        // The HWND has to exist before DWM attributes can be set. SourceInitialized fires
        // before the first paint, which avoids a visible opaque flash.
        if (window.IsInitialized && PresentationSource.FromVisual(window) is not null)
            Apply(window);
        else
            window.SourceInitialized += (_, _) => Apply(window);
    }

    /// <summary>Re-applies to every tracked window — used after a level or theme change.</summary>
    public static void ReapplyAll()
    {
        UpdateSharedBrushes();
        foreach (var window in Tracked.ToList())
            Apply(window);
    }

    /// <summary>The theme brush the window background re-binds to at level 0, so the
    /// opaque look follows the active theme (including the DarkBlue dictionary).</summary>
    private const string WindowSurfaceKey = "SystemControlBackgroundAltHighBrush";

    /// <summary>
    /// Chrome surfaces (toolbar, status bar) bind to RnChromeBrush and elevated cards to
    /// RnCardBrush, so they frost together with the window instead of sitting on it as
    /// opaque islands.
    /// </summary>
    private static void UpdateSharedBrushes()
    {
        var app = Application.Current;
        if (app is null) return;

        if (Level <= 0)
        {
            app.Resources["RnChromeBrush"] = new SolidColorBrush(ChromeColor());
            app.Resources["RnSurfaceBrush"] = new SolidColorBrush(SurfaceColor());
            app.Resources["RnCardBrush"] = new SolidColorBrush(CardColor());
            return;
        }

        var overlay = OverlayAlpha();
        // Chrome and cards keep a bit more body than the page so they stay legible at full frost.
        app.Resources["RnChromeBrush"] = new SolidColorBrush(WithAlpha(ChromeColor(), overlay + 28));
        app.Resources["RnSurfaceBrush"] = new SolidColorBrush(WithAlpha(SurfaceColor(), overlay));
        app.Resources["RnCardBrush"] = new SolidColorBrush(WithAlpha(CardColor(), Math.Min(255, overlay + 36)));
    }

    private static void Apply(Window window)
    {
        try
        {
            var source = (HwndSource?)PresentationSource.FromVisual(window);
            var handle = source?.Handle ?? new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            if (Level <= 0)
            {
                SetBackdrop(handle, BackdropType.None);
                SetAccent(handle, AccentState.Disabled, 0);
                SetCaptionColors(handle);
                SetTitleBarBackground(window, opaque: false);
                if (source?.CompositionTarget is not null)
                    source.CompositionTarget.BackgroundColor = Colors.White;
                window.SetResourceReference(Window.BackgroundProperty, WindowSurfaceKey);
                return;
            }

            // Rule 1: let whatever DWM draws behind the window show through.
            if (source?.CompositionTarget is not null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;

            // Rule 2: an accent policy would override the system backdrop.
            SetAccent(handle, AccentState.Disabled, 0);

            // Rule 3: no DwmExtendFrameIntoClientArea — that yields crisp Aero glass, not acrylic.
            var applied = SetBackdrop(handle, BackdropType.Acrylic);
            SetCaptionColors(handle);
            SetTitleBarBackground(window, opaque: true);
            if (!applied)
            {
                // Windows 10 has no system backdrop: fall back to the legacy acrylic accent,
                // which does blur and tint there (its alpha is honored on Win10).
                var tint = SurfaceColor();
                var alpha = (uint)Math.Max(OverlayAlpha(), 140); // keep the tint dominant
                var gradient = (alpha << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
                SetAccent(handle, AccentState.EnableAcrylicBlurBehind, gradient);
            }

            window.Background = new SolidColorBrush(WithAlpha(SurfaceColor(), OverlayAlpha()));
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to apply acrylic backdrop", ex);
        }
    }

    /// <summary>
    /// DWM paints the native caption buttons for this window regardless of the client
    /// surface. They are hidden under an opaque client, but once it goes see-through for the
    /// backdrop they appear a few pixels away from ModernWpf's own buttons — two visible
    /// sets. Nothing that stops DWM drawing them keeps the backdrop alive: disabling
    /// non-client rendering removes the backdrop too, and UseAeroCaptionButtons /
    /// GlassFrameThickness have no effect here.
    ///
    /// So cover them instead: ModernWpf's own title bar sits on top, and giving it an opaque
    /// background hides the native pair exactly the way the opaque window did at level 0.
    /// The cost is a solid title-bar strip above the frosted body — which matches the
    /// toolbar directly beneath it.
    /// </summary>
    private static void SetTitleBarBackground(Window window, bool opaque)
    {
        void Update()
        {
            if (opaque)
            {
                var brush = new SolidColorBrush(ChromeColor());
                ModernWpf.Controls.TitleBar.SetBackground(window, brush);
                ModernWpf.Controls.TitleBar.SetInactiveBackground(window, brush);
            }
            else
            {
                window.ClearValue(ModernWpf.Controls.TitleBar.BackgroundProperty);
                window.ClearValue(ModernWpf.Controls.TitleBar.InactiveBackgroundProperty);
            }
        }

        Update();
        // The title bar arrives with ModernWpf's window style, after SourceInitialized.
        window.Dispatcher.BeginInvoke(new Action(Update),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Level 1 → nearly solid surface, level 100 → pure acrylic (no overlay).</summary>
    private static int OverlayAlpha() =>
        (int)Math.Round(MaxOverlayAlpha * (1.0 - Level / 100.0));

    private static Color WithAlpha(Color color, int alpha) =>
        Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);

    private static bool IsDarkSurface() => ThemeManager.CurrentTheme switch
    {
        AppTheme.Light => false,
        AppTheme.DarkBlue or AppTheme.Dark => true,
        _ => ModernWpf.ThemeManager.Current.ActualApplicationTheme == ModernWpf.ApplicationTheme.Dark
    };

    /// <summary>Page surface, in the app's own palette — never derived from the desktop.</summary>
    private static Color SurfaceColor() => ThemeManager.CurrentTheme switch
    {
        AppTheme.DarkBlue => Color.FromRgb(0x0D, 0x1B, 0x2A),
        _ => IsDarkSurface() ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)
    };

    /// <summary>Toolbar / status bar color, matching the opaque chrome brush at level 0.</summary>
    private static Color ChromeColor() => ThemeManager.CurrentTheme switch
    {
        AppTheme.DarkBlue => Color.FromRgb(0x13, 0x25, 0x38),
        _ => IsDarkSurface() ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xE6, 0xE6, 0xE6)
    };

    /// <summary>Elevated cards (dashboard stats, profile details) — one step above the
    /// page surface so the elevation ramp actually reads in every theme.</summary>
    private static Color CardColor() => ThemeManager.CurrentTheme switch
    {
        AppTheme.DarkBlue => Color.FromRgb(0x16, 0x28, 0x3E),
        _ => IsDarkSurface() ? Color.FromRgb(0x2A, 0x2A, 0x2A) : Color.FromRgb(0xE9, 0xE9, 0xE9)
    };

    // ---- DWM system backdrop (Windows 11 22H2+) ----

    private enum BackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        /// <summary>DWMSBT_TRANSIENTWINDOW — the frosted "Acrylic" material.</summary>
        Acrylic = 3,
        MicaAlt = 4
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;

    private static bool SetBackdrop(IntPtr handle, BackdropType type)
    {
        try
        {
            // Tell DWM which way to tint, from OUR theme rather than the Windows setting —
            // otherwise a dark app on a light system gets a light frost behind dark text.
            var dark = IsDarkSurface() ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var value = (int)type;
            return DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref value, sizeof(int)) == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    // ---- Caption colors ----
    //
    // Windows paints the title bar with the user's accent color when "Show accent color on
    // title bars" is on, which would leave a solid, saturated caption sitting on top of the
    // frosted body — the window would read as two unrelated surfaces, and the app's look
    // would change with a system setting it has nothing to do with. Owning these three
    // attributes keeps the window's appearance the app's own decision.

    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private static void SetCaptionColors(IntPtr handle)
    {
        try
        {
            // With a backdrop, an unpainted caption becomes part of the frost; without one,
            // match the toolbar so the window still reads as a single surface.
            var caption = ToColorRef(ChromeColor());
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));

            var text = ToColorRef(ThemeManager.CurrentTheme switch
            {
                AppTheme.DarkBlue => Color.FromRgb(0xE8, 0xF0, 0xF8),
                _ => IsDarkSurface() ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x1A, 0x1A, 0x1A)
            });
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));

            var border = ToColorRef(ThemeManager.CurrentTheme switch
            {
                AppTheme.DarkBlue => Color.FromRgb(0x2D, 0x4A, 0x6E),
                _ => IsDarkSurface() ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xC8, 0xC8, 0xC8)
            });
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch (DllNotFoundException) { /* pre-Win11: attributes simply don't exist */ }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>COLORREF is 0x00BBGGRR, the reverse of the usual RGB packing.</summary>
    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- Legacy accent policy (Windows 10 fallback only) ----

    private const int WcaAccentPolicy = 19;

    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    private static void SetAccent(IntPtr handle, AccentState state, uint gradientColor)
    {
        var accent = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentState.Disabled ? 0u : 0x20u,
            GradientColor = gradientColor // 0xAABBGGRR
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                SizeOfData = size,
                Data = ptr
            };
            SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}
