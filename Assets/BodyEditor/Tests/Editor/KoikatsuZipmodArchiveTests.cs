using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BodyEditor.ReferenceModels;
using NUnit.Framework;

namespace BodyEditor.Tests
{
    public sealed class KoikatsuZipmodArchiveTests
    {
        [Test]
        public void ReadsSideloaderLooseTextureForVirtualBundle()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"koikatsu-loose-texture-{Guid.NewGuid():N}.zipmod");
            var expected = new byte[] { 1, 3, 5, 7 };
            try
            {
                using (var stream = File.Create(path))
                using (var zip = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           false))
                {
                    var entry = zip.CreateEntry(
                        "ABDATA/CHARA/TEXTURE/HIGHLIGHT/EYELIGHT128.PNG");
                    using (var target = entry.Open())
                    {
                        target.Write(expected, 0, expected.Length);
                    }
                }

                var archive = new KoikatsuZipmodArchive(
                    path,
                    new List<KoikatsuZipmodBundleDto>());

                Assert.That(
                    archive.TryReadLooseTexture(
                        "chara/texture/highlight.unity3d",
                        "eyelight128",
                        out var actual,
                        out var entryName),
                    Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(
                    entryName,
                    Is.EqualTo(
                        "ABDATA/CHARA/TEXTURE/HIGHLIGHT/EYELIGHT128.PNG"));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void DoesNotMatchLooseTextureFromAnotherVirtualBundle()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"koikatsu-loose-texture-{Guid.NewGuid():N}.zipmod");
            try
            {
                using (var stream = File.Create(path))
                using (var zip = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           false))
                {
                    zip.CreateEntry(
                        "abdata/chara/texture/eye/eyelight128.png");
                }

                var archive = new KoikatsuZipmodArchive(
                    path,
                    new List<KoikatsuZipmodBundleDto>());

                Assert.That(
                    archive.TryReadLooseTexture(
                        "chara/texture/highlight.unity3d",
                        "eyelight128",
                        out _,
                        out _),
                    Is.False);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void VirtualFileSystemPreservesOverrideOrder()
        {
            var firstPath = Path.Combine(
                Path.GetTempPath(),
                "first-overlay.zipmod");
            var secondPath = Path.Combine(
                Path.GetTempPath(),
                "second-overlay.zipmod");
            var firstBundle = new KoikatsuZipmodBundleDto
            {
                FullPath = "abdata/chara/overlay.unity3d",
                TrimmedPath = "chara/overlay.unity3d",
                StreamOffset = 123,
            };
            var secondBundle = new KoikatsuZipmodBundleDto
            {
                FullPath = "ABDATA/CHARA/OVERLAY.UNITY3D",
                TrimmedPath = "CHARA/OVERLAY.UNITY3D",
                StreamOffset = 456,
            };
            var first = new KoikatsuZipmodArchive(
                firstPath,
                new[] { firstBundle });
            var second = new KoikatsuZipmodArchive(
                secondPath,
                new[] { secondBundle });
            var fileSystem = new KoikatsuSideloaderVirtualFileSystem();
            fileSystem.AddArchive(first, new[] { firstBundle });
            fileSystem.AddArchive(second, new[] { secondBundle });

            var sources = new List<KoikatsuBundleSource>();
            fileSystem.AddBundleSources(
                "abdata/CHARA/overlay.unity3d",
                sources,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.That(sources, Has.Count.EqualTo(2));
            Assert.That(sources[0].FilePath, Is.EqualTo(firstPath));
            Assert.That(sources[0].StreamOffset, Is.EqualTo(123));
            Assert.That(
                sources[0].FallbackArchiveEntryName,
                Is.EqualTo("abdata/chara/overlay.unity3d"));
            Assert.That(sources[1].FilePath, Is.EqualTo(secondPath));
            Assert.That(sources[1].StreamOffset, Is.EqualTo(456));
            Assert.That(
                sources[1].FallbackArchiveEntryName,
                Is.EqualTo("ABDATA/CHARA/OVERLAY.UNITY3D"));
        }

        [Test]
        public void VirtualFileSystemReadsLooseTextureFromPureOverride()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"koikatsu-virtual-texture-{Guid.NewGuid():N}.zipmod");
            try
            {
                using (var stream = File.Create(path))
                using (var zip = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           false))
                {
                    var entry = zip.CreateEntry(
                        "abdata/chara/texture/highlight/overlay.png");
                    using (var target = entry.Open())
                    {
                        target.WriteByte(42);
                    }
                }

                var archive = new KoikatsuZipmodArchive(
                    path,
                    Array.Empty<KoikatsuZipmodBundleDto>());
                var fileSystem = new KoikatsuSideloaderVirtualFileSystem();
                fileSystem.AddArchive(
                    archive,
                    Array.Empty<KoikatsuZipmodBundleDto>(),
                    new[]
                    {
                        "abdata/chara/texture/highlight/overlay.png",
                    });

                Assert.That(
                    fileSystem.TryReadLooseTexture(
                        "chara/texture/highlight.unity3d",
                        "overlay",
                        out var data,
                        out var entryName,
                        out var archivePath),
                    Is.True);
                Assert.That(data, Is.EqualTo(new byte[] { 42 }));
                Assert.That(entryName, Does.EndWith("overlay.png"));
                Assert.That(archivePath, Is.EqualTo(path));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void SelectsNewestZipmodVersionPerGuid()
        {
            var older = CreateZipmod("mod.b", "1.2.0", "older.zipmod");
            var newer = CreateZipmod("mod.b", "v2.0", "newer.zipmod");
            var other = CreateZipmod("mod.a", "1.0.0", "other.zipmod");

            var selected = KoikatsuListCatalog.SelectActiveZipmods(
                new[] { older, newer, other });

            Assert.That(selected, Has.Count.EqualTo(2));
            Assert.That(selected[0], Is.SameAs(other));
            Assert.That(selected[1], Is.SameAs(newer));
        }

        [TestCase("r1.10-beta", "1.9", 1)]
        [TestCase("v2.0", "1.10", 1)]
        [TestCase("V1.0", "r1", 0)]
        [TestCase(null, "1", -1)]
        public void ComparesVersionsLikeSideloader(
            string first,
            string second,
            int expectedSign)
        {
            var comparison = KoikatsuListCatalog.CompareManifestVersions(
                first,
                second);

            Assert.That(Math.Sign(comparison), Is.EqualTo(expectedSign));
        }

        [Test]
        public void SelectsNewestWriteTimeWhenAnyVersionIsMissing()
        {
            var versioned = CreateZipmod("mod.a", "99.0", "old.zipmod");
            versioned.LastWriteTime = new DateTime(2025, 1, 1);
            var unversioned = CreateZipmod("mod.a", null, "new.zipmod");
            unversioned.LastWriteTime = new DateTime(2025, 1, 2);

            var selected = KoikatsuListCatalog.SelectActiveZipmods(
                new[] { versioned, unversioned });

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0], Is.SameAs(unversioned));
        }

        [Test]
        public void PreservesDiscoveryOrderWhenVersionsTie()
        {
            var first = CreateZipmod("mod.a", "1.0", "aaa.zipmod");
            var second = CreateZipmod("mod.a", "v1", "bbb.zipmod");

            var selected = KoikatsuListCatalog.SelectActiveZipmods(
                new[] { first, second });

            Assert.That(selected[0], Is.SameAs(first));
        }

        [Test]
        public void AppliesExactManifestMigrationWhenTargetIsActive()
        {
            var migrations = CreateMigrations(
                new KoikatsuMigrationInfoDto
                {
                    MigrationType = KoikatsuMigrationType.Migrate,
                    Category = 105,
                    GuidOld = "old.mod",
                    GuidNew = "new.mod",
                    IdOld = 12,
                    IdNew = 34,
                });
            var active = new HashSet<string>(
                new[] { "new.mod" },
                StringComparer.OrdinalIgnoreCase);
            var id = 12;
            var guid = " old.mod ";

            KoikatsuListCatalog.ApplyManifestMigration(
                migrations,
                active,
                105,
                ref id,
                ref guid);

            Assert.That(guid, Is.EqualTo("new.mod"));
            Assert.That(id, Is.EqualTo(34));
        }

        [Test]
        public void DoesNotMigrateToInactiveManifest()
        {
            var migrations = CreateMigrations(
                new KoikatsuMigrationInfoDto
                {
                    MigrationType = KoikatsuMigrationType.MigrateAll,
                    GuidOld = "old.mod",
                    GuidNew = "missing.mod",
                });
            var id = 12;
            var guid = "old.mod";

            KoikatsuListCatalog.ApplyManifestMigration(
                migrations,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                105,
                ref id,
                ref guid);

            Assert.That(guid, Is.EqualTo("old.mod"));
            Assert.That(id, Is.EqualTo(12));
        }

        [Test]
        public void StripAllClearsLegacyManifestGuid()
        {
            var migrations = CreateMigrations(
                new KoikatsuMigrationInfoDto
                {
                    MigrationType = KoikatsuMigrationType.StripAll,
                    GuidOld = "old.mod",
                });
            var id = 12;
            var guid = "old.mod";

            KoikatsuListCatalog.ApplyManifestMigration(
                migrations,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                105,
                ref id,
                ref guid);

            Assert.That(guid, Is.Empty);
            Assert.That(id, Is.EqualTo(12));
        }

        private static IReadOnlyDictionary<
            string,
            List<KoikatsuMigrationInfoDto>> CreateMigrations(
                params KoikatsuMigrationInfoDto[] migrations)
        {
            return new Dictionary<string, List<KoikatsuMigrationInfoDto>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["old.mod"] = new List<KoikatsuMigrationInfoDto>(migrations),
            };
        }

        private static KoikatsuZipmodInfoDto CreateZipmod(
            string guid,
            string version,
            string fileName)
        {
            return new KoikatsuZipmodInfoDto
            {
                FileName = fileName,
                Manifest = new KoikatsuZipmodManifestDto
                {
                    Guid = guid,
                    Version = version,
                },
            };
        }
    }
}
