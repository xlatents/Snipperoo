using System.Collections.Specialized;
using System.Drawing;
using System.Windows.Forms;

namespace Snipperoo.Output;

internal static class ClipboardFile
{
    private const int DropEffectCopy = 1;

    /// <summary>
    /// Puts <paramref name="path"/> on the clipboard as a file (like Ctrl+C in Explorer), so pasting into
    /// Discord or a folder attaches the file itself. With <paramref name="image"/>, image editors can paste
    /// the pixels too. Must run on an STA thread.
    /// </summary>
    public static void Copy(string path, Image? image = null)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        // Tells Explorer to copy rather than move on paste.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(DropEffectCopy)));
        if (image is not null)
            data.SetImage(image);
        Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 50);
    }
}
