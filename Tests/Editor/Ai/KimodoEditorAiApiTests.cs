using System;
using System;
using System.Collections.Generic;
using System.IO;
using KimodoUnityMotionTools.ProjectEditor.Ai;
using NUnit.Framework;

namespace KimodoUnityMotionTools.Tests
{
    [TestFixture]
    public sealed class KimodoEditorAiApiTests
    {
        [Test]
        public void GetDefaultRuntimeRoot_NotEmpty()
        {
            string path = KimodoEditorAiApi.GetDefaultRuntimeRoot();
            Assert.IsFalse(string.IsNullOrWhiteSpace(path));
        }

        [Test]
        public void ResolveLauncherOrThrow_InvalidRoot_Throws()
        {
            string invalid = Path.Combine(Path.GetTempPath(), "Kimodo_Invalid_" + Guid.NewGuid().ToString("N"));
            var ex = Assert.Throws<FileNotFoundException>(() => KimodoEditorAiApi.ResolveLauncherOrThrow(invalid));
            Assert.IsNotNull(ex);
        }

        [Test]
        public void QueryModels_FiltersDisplayableNames()
        {
            string root = Path.Combine(Path.GetTempPath(), "Kimodo_QueryModels_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Kimodo-SOMA-RP-v1"));
                Directory.CreateDirectory(Path.Combine(root, "llama-3.1-8b"));
                Directory.CreateDirectory(Path.Combine(root, "misc-folder"));

                List<KimodoEditorAiApi.ModelDirectoryDto> models = KimodoEditorAiApi.QueryModels(root);
                Assert.IsNotNull(models);
                Assert.IsTrue(models.Exists(m => m.Name.Equals("Kimodo-SOMA-RP-v1", StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(models.Exists(m => m.Name.Equals("llama-3.1-8b", StringComparison.OrdinalIgnoreCase)));
                Assert.IsFalse(models.Exists(m => m.Name.Equals("misc-folder", StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
