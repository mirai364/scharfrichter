using Scharfrichter.Codec;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Scharfrichter.Tests
{
    /// <summary>
    /// Unit tests for Subfolder.Parse, used to expand file/directory arguments.
    /// </summary>
    public class SubfolderTests
    {
        [Fact]
        public void FileReturnsItself()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                string[] result = Subfolder.Parse(tempFile);
                Assert.Equal(new[] { tempFile }, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void MissingPathReturnsEmptyArray()
        {
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Assert.Empty(Subfolder.Parse(missing));
        }

        [Fact]
        public void DirectoryReturnsAllFilesRecursively()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            try
            {
                string fileA = Path.Combine(root, "a.txt");
                string fileB = Path.Combine(sub, "b.txt");
                File.WriteAllText(fileA, "a");
                File.WriteAllText(fileB, "b");

                string[] result = Subfolder.Parse(root).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Assert.Equal(new[] { fileA, fileB }.OrderBy(x => x, StringComparer.Ordinal).ToArray(), result);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void MultiplePathsAreCombined()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                string[] result = Subfolder.Parse(new[] { tempFile, missing });
                Assert.Equal(new[] { tempFile }, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}