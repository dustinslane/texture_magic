using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using CodeWalker;

namespace TextureMagic;

public class CodeWalkerXml
{
    private string _path;
    
    public CodeWalkerXml(string path)
    {
        _path = path;
    }

    public void Load(int height, int width, int newHeight, int newWidth)
    {
        float heightRatio = (float)newHeight / (float)height;
        float widthRatio = (float)newWidth / (float)width;
        
        int texture0 = -1; int texture1 = -1; int texture2 = -1;
        XElement doc = XElement.Load(_path);
        var drawables = from item in doc.Descendants("Item") select item.Descendants("VertexBuffer");
        foreach (var drawable in drawables)
        {
            foreach (var buff in drawable)
            {
                var layout = buff.Descendants("Layout").ToList();
                int counter = 0;
                foreach (var el in layout.Descendants())
                {
                    string name = el.Name.ToString();
                    switch (name)
                    {
                        case "TexCoord0":
                            texture0 = counter;
                            break;
                        case "TexCoord1":
                            texture1 = counter;
                            break;
                        case "TexCoord2":
                            texture2 = counter;
                            break;
                    }

                    counter++;
                }

                var dataElements = buff.Descendants("Data");
                if (!dataElements.Any())
                {
                    dataElements = buff.Descendants("Data1");
                }
                if (!dataElements.Any())
                {
                    dataElements = buff.Descendants("Data2");
                }

                var data = dataElements.First();
                string[] lines = data.Value.Split("\n");
                for (var i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int spacesPreceding = 0;
                    for (int x = 0; x < lines[i].Length; x++)
                    {
                        if (lines[i][x] == ' ')
                        {
                            spacesPreceding ++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    string[] segments = line.Split("   ");

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    for (int j = 0; j < segments.Length; j++)
                    {
                        if (j == texture0 || j == texture1 || j == texture2)
                        {
                            // this is a texture line
                            // convert the items to vector2
                            string segment = segments[j];
                            string[] vectorElements = segment.Split(" ");
                            Vector2 texCoord = new Vector2(float.Parse(vectorElements[0]), float.Parse(vectorElements[1]));
                            
                            // translate vector

                            texCoord.X = texCoord.X - texCoord.X / widthRatio;
                            texCoord.Y = texCoord.Y - texCoord.Y / heightRatio;
                            
                            texCoord.X = texCoord.X  / widthRatio;
                            texCoord.Y = texCoord.Y  / heightRatio;
 
                            vectorElements[0] = texCoord.X.ToString();
                            vectorElements[1] = texCoord.Y.ToString();

                            segment = string.Join(' ', vectorElements);
                            segments[j] = segment;
                        }
                    }

                    line = string.Join("   ", segments);
                    lines[i] = new string(' ', spacesPreceding) + line;
                }

                data.Value = string.Join('\n', lines);
            }
           
            break;
        }

        string path = Directory.GetParent(_path) + "\\ass.ydd.xml";
        if (File.Exists(path)) // Delete the file, because otherwise weird shit is going to happen
        {
            File.Delete(path);
        }
        
        using var file = File.OpenWrite(path);
        using var outputStream = new StreamWriter(file);
        XmlWriterSettings xmlWriterSettings = new XmlWriterSettings()
        {
            Indent = true,
            IndentChars = " ",
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace,
            Encoding = Encoding.UTF8,
        };
        using (var writer = XmlWriter.Create(outputStream, xmlWriterSettings))
        {
            doc.Save(writer);
        }
    }
}