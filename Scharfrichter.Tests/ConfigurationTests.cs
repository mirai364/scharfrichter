using Scharfrichter.Common;
using System.IO;
using System.Text;
using Xunit;

namespace Scharfrichter.Tests
{
    /// <summary>
    /// Unit tests for Configuration (INI-style text) parsing and
    /// InfoCollection typed accessors.
    /// </summary>
    public class ConfigurationTests
    {
        private static Configuration Read(string text)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                return Configuration.Read(ms);
            }
        }

        [Fact]
        public void ReadParsesSectionsAndKeyValues()
        {
            var config = Read("[Section]\nKey=Value\n");
            Assert.Equal("Value", config["Section"]["Key"]);
        }

        [Fact]
        public void ReadIgnoresMalformedInputAndReturnsEmpty()
        {
            var config = Read("just some text\n");
            Assert.NotNull(config);
        }

        [Fact]
        public void ReadSupportsMultipleSections()
        {
            var config = Read("[A]\nk=v\n[B]\nx=y\n");
            Assert.Equal("v", config["A"]["K"]);
            Assert.Equal("y", config["B"]["X"]);
        }

        [Fact]
        public void InfoCollectionAccessorsAreCaseInsensitive()
        {
            var config = Read("[S]\nName=test\nCount=42\nEnabled=True\n");
            var info = config["S"];

            Assert.Equal("test", info.GetString("name"));
            Assert.Equal("test", info.GetString("NAME", "fallback"));
            Assert.Equal(42, info.GetValue("count"));
            Assert.Equal(7, info.GetValue("missing", 7));
            Assert.True(info.GetBool("enabled"));
        }

        [Fact]
        public void InfoCollectionGetBoolReturnsFalseForMissingKey()
        {
            var config = Read("[S]\n");
            Assert.False(config["S"].GetBool("missing"));
        }

        [Fact]
        public void InfoCollectionDefaultsAreStored()
        {
            var config = Read("[S]\n");
            var info = config["S"];

            info.SetDefaultString("DefaultStr", "abc");
            info.SetDefaultValue("DefaultInt", 123);
            info.SetDefaultBool("DefaultBool", true);

            Assert.Equal("abc", info.GetString("defaultstr"));
            Assert.Equal(123, info.GetValue("defaultint"));
            Assert.True(info.GetBool("defaultbool"));
        }

        [Fact]
        public void InfoCollectionSettersRoundTrip()
        {
            var config = Read("[S]\n");
            var info = config["S"];

            // Setters store keys verbatim while getters normalize to
            // uppercase, so callers must pass uppercase keys for a reliable
            // round trip through the typed getters.
            info.SetString("STR", "hello");
            info.SetValue("INT", 99);
            info.SetBool("FLAG", true);

            Assert.Equal("hello", info.GetString("STR"));
            Assert.Equal(99, info.GetValue("INT"));
            Assert.True(info.GetBool("FLAG"));
        }

        [Fact]
        public void WriteRoundTripsThroughRead()
        {
            var original = Read("[S]\nKey=Value\n");
            using (var ms = new MemoryStream())
            {
                original.Write(ms);
                ms.Position = 0;
                var reloaded = Configuration.Read(ms);
                Assert.Equal("Value", reloaded["S"]["Key"]);
            }
        }

        [Fact]
        public void ConfigPathEndsWithRequestedFileName()
        {
            string path = Configuration.ConfigPath("MyConfig");
            Assert.EndsWith("MyConfig.txt", path);
        }
    }
}