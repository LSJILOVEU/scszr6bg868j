using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // Shared fixtures for the E1a map description tests. Sample is the frozen example of the contract (appendix B.1),
    // byte for byte; error cases change it in exactly one place and name lines by its layout.
    internal static class E1aSamples
    {
        internal const string TerrainPath = "Assets/WarCore/Maps/terrain-demo.map.json";
        internal const string M6Path = "Assets/WarCore/Maps/m6-demo.map.json";

        private static readonly string[] SampleLines =
        {
            "{",
            "  \"format\": \"warcore.map\",",
            "  \"version\": 1,",
            "  \"id\": \"sample\",",
            "  \"content\": \"rwr\",",
            "  \"pack\": \"rwr-m6\",",
            "  \"size\": [2, 1],",
            "  \"origin\": [-1, 0],",
            "  \"floorRaw\": -65536,",
            "  \"ceilingRaw\": 262144,",
            "  \"layers\": [",
            "    {",
            "      \"name\": \"walls\",",
            "      \"bit\": 1",
            "    }",
            "  ],",
            "  \"heights\": [",
            "    [0, 0, 0],",
            "    [-65536, 0, 0]",
            "  ],",
            "  \"surfaces\": {",
            "    \"names\": [\"grass\", \"mud\"],",
            "    \"rows\": [",
            "      \"AB\"",
            "    ]",
            "  },",
            "  \"walls\": [",
            "    {",
            "      \"id\": 1,",
            "      \"kind\": \"wall\",",
            "      \"layer\": \"walls\",",
            "      \"cell\": [1, 0],",
            "      \"size\": [1, 1],",
            "      \"heightRaw\": 131072,",
            "      \"owner\": 0",
            "    }",
            "  ],",
            "  \"points\": [",
            "    {",
            "      \"id\": 1,",
            "      \"at\": [-500, 500],",
            "      \"radiusMm\": 400,",
            "      \"initialOwner\": 0,",
            "      \"spawn\": [-500, 500]",
            "    }",
            "  ],",
            "  \"bases\": [",
            "    {",
            "      \"id\": 1,",
            "      \"team\": 2,",
            "      \"at\": [-500, 200]",
            "    }",
            "  ],",
            "  \"units\": [",
            "    {",
            "      \"key\": 1,",
            "      \"type\": \"rwr.soldier-1\",",
            "      \"team\": 1,",
            "      \"squad\": 7,",
            "      \"at\": [-200, 800],",
            "      \"reserve\": false,",
            "      \"owner\": 1,",
            "      \"src\": {",
            "        \"instance\": \"s1\",",
            "        \"kind\": \"unit\",",
            "        \"src\": 3",
            "      }",
            "    }",
            "  ],",
            "  \"run\": {",
            "    \"seed\": 41,",
            "    \"crowdPass\": \"none\"",
            "  },",
            "  \"owners\": [\"hand\", \"stamp:s1\"],",
            "  \"heightOwner\": [",
            "    \"...\",",
            "    \".A.\"",
            "  ],",
            "  \"surfaceOwner\": [",
            "    \".B\"",
            "  ],",
            "  \"stamps\": [",
            "    {",
            "      \"instance\": \"s1\",",
            "      \"kind\": \"unit\",",
            "      \"src\": 3,",
            "      \"id\": 1",
            "    }",
            "  ],",
            "  \"removed\": [",
            "    {",
            "      \"instance\": \"s1\",",
            "      \"kind\": \"wall\",",
            "      \"src\": 2",
            "    }",
            "  ],",
            "  \"next\": {",
            "    \"wall\": 2,",
            "    \"point\": 2,",
            "    \"base\": 2,",
            "    \"unit\": 2",
            "  }",
            "}",
        };

        internal static readonly string Sample = string.Join("\n", SampleLines) + "\n";

        internal static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

        // Replaces the only occurrence of oldText.
        internal static string Edit(string text, string oldText, string newText)
        {
            int at = text.IndexOf(oldText, StringComparison.Ordinal);
            Assert.That(at, Is.Not.EqualTo(-1), "fixture text not found: " + oldText);
            Assert.That(text.IndexOf(oldText, at + 1, StringComparison.Ordinal), Is.EqualTo(-1), "fixture text is not unique: " + oldText);
            return text.Substring(0, at) + newText + text.Substring(at + oldText.Length);
        }

        internal static MapBakeResult Bake(byte[] bytes)
        {
            MapBakeResult result = null;
            Assert.DoesNotThrow(() => result = MapBake.Run(bytes));
            return result;
        }

        internal static MapBakeResult Bake(string text) => Bake(Utf8(text));

        internal static InvalidDataException Fails(byte[] bytes) => Assert.Throws<InvalidDataException>(() => MapBake.Run(bytes));

        internal static string Error(string text) => Fails(Utf8(text)).Message;
    }
}
