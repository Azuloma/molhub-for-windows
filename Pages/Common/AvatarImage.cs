using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace FusionLedger.Windows;

/// <summary>
/// Shows a validated PNG avatar in a PersonPicture. A picture that is out of the tree when the theme changes comes
/// back with only the initials, so the image is decoded again every time the picture is loaded or its theme changes.
/// Each picture has one handler pair holding its current image: attaching again replaces the image (an older one never
/// comes back on the next load or theme change) and <see cref="Clear"/> returns to the initials.
/// </summary>
internal static class AvatarImage
{
    private sealed class State
    {
        public byte[]? Png { get; set; }
        public int DecodePixelWidth { get; set; }
        /// <summary>Bumped by every change; a decode that finishes after a newer change is dropped.</summary>
        public int Version { get; set; }
    }

    private static readonly ConditionalWeakTable<PersonPicture, State> States = new();

    public static void Attach(PersonPicture picture, byte[] png, int decodePixelWidth)
    {
        var state = StateFor(picture);
        state.Png = png;
        state.DecodePixelWidth = decodePixelWidth;
        state.Version++;
        if (picture.IsLoaded) _ = ApplyAsync(picture, state);
    }

    /// <summary>Removes the image so the initials show (for example after the icon was removed).</summary>
    public static void Clear(PersonPicture picture)
    {
        if (States.TryGetValue(picture, out var state))
        {
            state.Png = null;
            state.Version++;
        }
        picture.ProfilePicture = null;
    }

    private static State StateFor(PersonPicture picture)
    {
        if (States.TryGetValue(picture, out var existing)) return existing;
        var state = new State();
        States.Add(picture, state);
        picture.Loaded += (sender, e) => _ = ApplyAsync((PersonPicture)sender, state);
        picture.ActualThemeChanged += (sender, e) => _ = ApplyAsync((PersonPicture)sender, state);
        return state;
    }

    private static async Task ApplyAsync(PersonPicture picture, State state)
    {
        if (state.Png is not { } png) return;
        var version = state.Version;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = state.DecodePixelWidth };
            await image.SetSourceAsync(stream);
            if (version == state.Version) picture.ProfilePicture = image;
        }
        catch
        {
            // The initials fallback stays in place.
        }
    }
}
