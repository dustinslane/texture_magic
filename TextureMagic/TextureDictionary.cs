using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeWalker.GameFiles;

namespace TextureMagic;

public class TextureDictionary
{
    private string _path;
    private YtdFile? _ytd;
    public List<Texture> Textures { get; private set; } = new ();
    public TextureDictionary(string path)
    {
        _path = path;
    }

    public void Rebuild(List<Texture> textures)
    {
        _ytd?.TextureDict.BuildFromTextureList(textures);
    }
    
    public Task Load()
    {
        _ytd = new YtdFile();
        var data = File.ReadAllBytes(_path);
        JenkIndex.Ensure(Path.GetFileNameWithoutExtension(_path));
        RpfFile.LoadResourceFile<YtdFile>(_ytd, data, 165U);
        Textures = _ytd.TextureDict.Textures.data_items.ToList();
        return Task.FromResult(0);
    }

    public async Task SaveToDisk()
    {
        if (_ytd == null)
        {
            throw new Exception("YTD was not initialized before saving");
        }
        await File.WriteAllBytesAsync(_path, _ytd.Save());
    }
}