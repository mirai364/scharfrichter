using Scharfrichter.Common;
using System;
using System.IO;
using Xunit;

using CommonHelper = Scharfrichter.Common.Common;

namespace Scharfrichter.Tests
{
    /// <summary>
    /// Unit tests for Scharfrichter.Common helpers (nameReplace and
    /// SafeCreateDirectory).
    /// </summary>
    public class CommonTests
    {
        [Fact]
        public void NameReplaceSubstitutesColon()
        {
            Assert.Equal("a：b", CommonHelper.nameReplace("a:b"));
        }

        [Fact]
        public void NameReplaceSubstitutesWindowsForbiddenCharacters()
        {
            Assert.Equal("a_b_c_d_e_f_g", CommonHelper.nameReplace("a/b\\c\"d*e|f?g"));
        }

        [Fact]
        public void NameReplaceHandlesTrailingDots()
        {
            Assert.Equal("a…", CommonHelper.nameReplace("a..."));
            Assert.Equal("a_", CommonHelper.nameReplace("a.."));
            Assert.Equal("a_", CommonHelper.nameReplace("a."));
        }

        [Fact]
        public void NameReplaceLeavesValidNamesUntouched()
        {
            Assert.Equal("abc_123.txt", CommonHelper.nameReplace("abc_123.txt"));
        }

        [Fact]
        public void SafeCreateDirectoryCreatesMissingDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                DirectoryInfo created = CommonHelper.SafeCreateDirectory(path);
                Assert.NotNull(created);
                Assert.True(Directory.Exists(path));
            }
            finally
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }

        [Fact]
        public void SafeCreateDirectoryReturnsNullWhenDirectoryExists()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                Assert.Null(CommonHelper.SafeCreateDirectory(path));
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }
    }
}