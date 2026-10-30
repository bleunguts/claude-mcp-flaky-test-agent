using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;

public class Helpers
{
    public static string FindSlnFolderRecursively(string folder)
    {
        var currentFolder = Directory.GetParent(folder);
        var fileInfo = currentFolder.GetFiles("FlakyDetective.slnx", SearchOption.TopDirectoryOnly);
        if (fileInfo.Length > 0)
        {
            return currentFolder.FullName;
        }

        if (currentFolder.Parent == null)
        {
            throw new Exception("Could not find sln folder");
        }

        return FindSlnFolderRecursively(currentFolder.FullName);
    }
}
