using System.Text.Json;

namespace Steward.Core;

public static class AtomicState
{
    public static T ReadOrMigrate<T>(string path,Func<T> migrate)
    {
        try
        {
            using var file=File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(file)??throw new InvalidDataException("Empty safety state");
        }
        catch(FileNotFoundException){return migrate();}
        catch(DirectoryNotFoundException){return migrate();}
    }
    public static void Write<T>(string path,T state)
    {
        var directory=Path.GetDirectoryName(path)??throw new InvalidDataException("Safety state needs a directory");
        Directory.CreateDirectory(directory);
        var temporary=Path.Combine(directory,Path.GetFileName(path)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(state);
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
            {file.Write(bytes);file.Flush(true);}
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
