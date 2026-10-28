namespace Crown.Infrastructure.Diagnostics;

/// <summary>
/// UTF-8 BOM — иначе Notepad на Windows открывает логи как CP1251 («РІСЂРµРјСЏ» вместо «время»).
/// </summary>
public static class Utf8TextFile
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static byte[] EnsureBom(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length >= 3
            && content[0] == Bom[0]
            && content[1] == Bom[1]
            && content[2] == Bom[2])
        {
            return content;
        }

        var withBom = new byte[content.Length + Bom.Length];
        Buffer.BlockCopy(Bom, 0, withBom, 0, Bom.Length);
        Buffer.BlockCopy(content, 0, withBom, Bom.Length, content.Length);
        return withBom;
    }
}
