using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace neat;

/// <summary>
/// The texture of the window colour, as in Zen Browser. A layer of fine noise
/// lies over the window's colour, under the title bar and the sidebar, and
/// the dial in the colour menu sets how strongly it shows. The page area is not
/// affected: it is covered by the web view anyway. The dial itself is in
/// picker.dial.cs. The maths is in <see cref="Grain"/>.
///
/// The noise is one small tile made when it is first needed and repeated over
/// the window as many times as it takes (a window has no way to repeat an image
/// by itself).
/// </summary>
public sealed partial class Win
{
    private byte[]? _tileBytes;        // the noise tile's pixels, made once
    private WriteableBitmap? _tile;     // the tile as a bitmap, for the window's layer
    private int _sideRows;              // how many tiles tall the sidebar panel's noise layer is (0: none yet)
    private int _sideCols;              // how many tiles wide it is (0: none yet)

    private void GrainInit()
    {
        // The tiles have to cover the window, so they follow its size.
        root.SizeChanged += (s, e) =>
        {
            if (App.Look.Cur.Texture > 0)
            {
                Tiles();
                SideGrain();
            }
        };
    }

    /// <summary>Brings the grain layer up to date with the current texture.</summary>
    private void GrainPaint()
    {
        var t = App.Look.Cur;
        var on = t.Texture > 0;

        grain.Opacity = Grain.Opacity(t.Texture);
        grain.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
            Tiles();
        SideGrain();
    }

    // ---- the noise tiles ----

    /// <summary>Makes the noise tile the first time it is needed.</summary>
    private WriteableBitmap Tile()
    {
        _tile ??= Bitmap(Grain.TileSize, Grain.TileSize, TileBytes());
        return _tile;
    }

    private byte[] TileBytes()
    {
        _tileBytes ??= Grain.Tile();
        return _tileBytes;
    }

    /// <summary>A bitmap of the given size holding the given pixels (blue, green, red, alpha; colour already multiplied by alpha).</summary>
    private static WriteableBitmap Bitmap(int width, int height, byte[] pixels)
    {
        var bitmap = new WriteableBitmap(width, height);

        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels, 0, pixels.Length);

        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>
    /// Lays out enough copies of the tile to cover the window, adding or
    /// removing copies as the window grows or shrinks. Cheap to call often.
    /// </summary>
    private void Tiles()
    {
        var size = Grain.TileSize;
        var columns = (int)Math.Ceiling(root.ActualWidth / size);
        var rows = (int)Math.Ceiling(root.ActualHeight / size);
        var need = columns * rows;

        for (var i = 0; i < need; i++)
        {
            Image image;

            if (i < grain.Children.Count)
            {
                image = (Image)grain.Children[i];
            }
            else
            {
                image = new Image
                {
                    Source = Tile(),
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false,
                };
                grain.Children.Add(image);
            }

            Canvas.SetLeft(image, i % columns * size);
            Canvas.SetTop(image, i / columns * size);
        }

        while (grain.Children.Count > need)
            grain.Children.RemoveAt(grain.Children.Count - 1);
    }

    // ---- the floating sidebar ----

    /// <summary>
    /// The sidebar, when it floats over the page (hidden, or peeking), is a solid
    /// panel, and it hides the window's grain behind it. So the panel carries a
    /// layer of its own, under its content: a brush made of the noise tile repeated
    /// across and downwards (a brush cannot repeat by itself, so the repeats are
    /// built into one bitmap), which a rounded border then clips to the panel's
    /// corners. The bitmap has to be as wide as the widest the panel gets (the open
    /// Library, LibW) and as tall as the window: a brush does not repeat sideways,
    /// so any part of the panel beyond the bitmap would have no noise. The layer is
    /// pulled out over the panel's padding and its border so that it covers the
    /// whole panel, and it is hidden while the sidebar is docked with no Library
    /// open, because then the window's own grain shows through.
    /// Called whenever the texture, the window's size or the sidebar's state change.
    /// </summary>
    private void SideGrain()
    {
        var t = App.Look.Cur;
        var show = t.Texture > 0 && (!_dock || _lib);

        sidegrain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;

        sidegrain.Opacity = Grain.Opacity(t.Texture);

        // Out over the padding and over the border too, or a line the width of the border
        // would be left without noise all round the panel.
        var pad = side.Padding;
        var edge = side.BorderThickness;
        sidegrain.Margin = new Thickness(
            -(pad.Left + edge.Left),
            -(pad.Top + edge.Top),
            -(pad.Right + edge.Right),
            -(pad.Bottom + edge.Bottom));

        // The layer has to be as wide as the widest the panel gets and as tall as the window.
        // It only ever grows: a layer that is too big is cut off by the panel.
        var cols = Math.Max(1, (int)Math.Ceiling((LibW + Bite) / Grain.TileSize));
        var rows = Math.Max(1, (int)Math.Ceiling(root.ActualHeight / Grain.TileSize));
        if (rows > _sideRows || cols > _sideCols)
        {
            cols = Math.Max(cols, _sideCols);
            rows = Math.Max(rows, _sideRows);

            var size = Grain.TileSize;
            var tile = TileBytes();
            var line = size * 4;            // bytes in one pixel row of a tile
            var stride = cols * line;       // bytes in one pixel row of the whole layer
            var layer = new byte[stride * size * rows];

            // Every pixel row of the layer is the same tile row, cols times over.
            for (var r = 0; r < rows; r++)
            {
                for (var y = 0; y < size; y++)
                {
                    for (var c = 0; c < cols; c++)
                        Buffer.BlockCopy(tile, y * line, layer, (r * size + y) * stride + c * line, line);
                }
            }

            sidegrain.Background = new ImageBrush
            {
                ImageSource = Bitmap(size * cols, size * rows, layer),
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            _sideRows = rows;
            _sideCols = cols;
        }
    }
}
