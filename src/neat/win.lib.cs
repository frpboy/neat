using Microsoft.UI.Xaml;

namespace neat;

/// <summary>
/// The Library: a mode of the sidebar, not a window. While it is open the tab list, the
/// "new tab" row and the footer give way to the Library view (lib/libview.xaml), and the
/// sidebar grows wider, over the page.
///
/// The page is never moved: the column that reserves room for a docked sidebar (col0,
/// see Layout in win.side.cs) keeps its width, only the sidebar element itself gets
/// wider and covers more of the page. Where it covers the page it has the opaque
/// gradient the floating sidebar already uses (PeekBrush), so the page does not show through.
///
/// It works from all three sidebar states. From the hidden one the sidebar floats (a peek)
/// and stays up while the Library is open; the pointer leaving does not close it.
/// The shortcuts that open it are in win.keys.cs.
/// </summary>
public sealed partial class Win
{
    // Width of the sidebar while the Library is open (the panel, padding included).
    private const double LibW = 420;

    private bool _lib;

    // The section the Library opens on when it is opened from the footer button: the last one used.
    private Sec _sec = Sec.History;

    private void LibInit()
    {
        lib.Back += LibShut;
        lib.Opened += url => _ = Open(url);

        // A bookmark or history entry went: the star in the address bar and the footer counters depend on them.
        lib.Edited += () =>
        {
            _ = Star();
            _ = Stats();
        };
    }

    /// <summary>Opens the Library on a section. If it is open already, switches to that section and reloads it.</summary>
    private void ShowLib(Sec sec)
    {
        _sec = sec;

        if (!_lib)
        {
            _lib = true;

            // Hidden sidebar: let it float over the page. Nothing happens when it is docked.
            Peek(true);

            newtab.Visibility = Visibility.Collapsed;
            list.Visibility = Visibility.Collapsed;
            foot.Visibility = Visibility.Collapsed;
            lib.Visibility = Visibility.Visible;

            Layout();
        }

        lib.Show(sec);
    }

    /// <summary>
    /// Closes the Library and brings the tab list back. A floating sidebar then closes itself
    /// as usual once the pointer is away from it.
    /// </summary>
    private void LibShut()
    {
        if (!_lib)
            return;

        _lib = false;

        lib.Park();
        lib.Visibility = Visibility.Collapsed;
        newtab.Visibility = Visibility.Visible;
        list.Visibility = Visibility.Visible;
        foot.Visibility = Visibility.Visible;

        // Back to the sidebar's own width, background and grain.
        Layout();

        // If the pointer is already away, a floating sidebar should not hang on forever.
        Leave();
    }

    /// <summary>
    /// What the Library changes about the sidebar, put on top of what Layout() set. Layout() calls
    /// this at its end and does nothing else differently, so the sidebar's own geometry is untouched.
    /// </summary>
    private void LibLayout()
    {
        if (!_lib)
            return;

        // A floating sidebar is Bite wider than its panel (it bites into the window border).
        side.Width = LibW + (_dock ? 0 : Bite);

        // Docked, the sidebar is see-through (it sits on the window's own gradient). Wider than
        // that spot it would show the page through, so it gets the same opaque copy of the
        // gradient a floating one has, and the same edge.
        side.Background = PeekBrush();
        side.BorderBrush = _edge;
    }

    private void libbtn_Click(object sender, RoutedEventArgs e)
    {
        ShowLib(_sec);
    }
}
