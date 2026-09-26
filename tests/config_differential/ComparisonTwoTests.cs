using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CameraUnlock.Core.Config;
using CameraUnlock.Core.Input;
using SuperliminalHeadTracking.Config;
using SuperliminalHeadTracking.Legacy;
using Xunit;

namespace SuperliminalHeadTracking.Tests.Differential
{
    /// <summary>
    /// Comparison 2, the import against the migration, and what the import does with each value
    /// v0.2.0 ran on. Every input of comparison 1 is migrated into a new CameraUnlock.ini, from a
    /// writable and from a read-only legacy file, once over the built-in Defaults.ini and once over
    /// a Defaults.ini that differs on every row this game takes from it.
    /// </summary>
    public class ComparisonTwoTests
    {
        /// <summary>Every global row the table binds, each away from its built-in value.</summary>
        private const string OtherDefaults =
            "[CameraUnlock]\r\nConfigFormat=1\r\n\r\n" +
            "[Network]\r\nUdpPort=4343\r\n\r\n" +
            "[General]\r\nEnableOnStartup=false\r\nWorldSpaceYaw=false\r\nRotationEnabled=true\r\n\r\n" +
            "[Smoothing]\r\nLocalSmoothing=0.25\r\nRemoteSmoothing=0.35\r\n\r\n" +
            "[Position]\r\nPositionEnabled=false\r\nPositionLimitX=0.26\r\nPositionLimitY=0.16\r\nPositionLimitYDown=0.17\r\n" +
            "PositionLimitZ=0.36\r\nPositionLimitZBack=0.06\r\nCollisionEnabled=false\r\nCollisionReleaseSmoothing=0.5\r\n" +
            "TrackerPivotForward=0.05\r\n\r\n" +
            "[Hotkeys]\r\nToggleKey=F8\r\nCycleTrackingModeKey=F7\r\nYawModeKey=F6\r\n";

        // A v0.2.0 .cfg can hold a number for a key, which BepInEx's enum parse accepts and Unity
        // names no key for. No hotkey list can hold it and no approved rule drops it, so the config
        // owner defers these imports: the player keeps what v0.2.0 ran on, nothing is written, and
        // the import runs again at the next start.
        // These are unresolved, not accepted: core's config-format.json has no rule for them yet
        // (N1 covers native virtual-key codes only). Once it records one, the map applies it and
        // this list is deleted. An input outside it that the codecs cannot hold still fails here.
        private static readonly string[] DeferredValues = { "value 010", "value -1", "value +1", "value space then 1", "value 1 then space", "value 2" };

        private static IEnumerable<string> Deferred()
        {
            return new[] { "[Keybindings] ToggleKey", "[Keybindings] CycleTrackingModeKey", "[Keybindings] YawModeKey" }
                .SelectMany(key => DeferredValues.Select(v => "corpus " + key + ": " + v))
                .OrderBy(n => n, StringComparer.Ordinal);
        }

        private static readonly Lazy<string> MigratedDir = new Lazy<string>(() =>
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migrated");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            return dir;
        });

        [Fact]
        public void TheMigrationHoldsWhatTheImportReadOverTheBuiltInDefaults()
        {
            Compare(null);
        }

        [Fact]
        public void TheMigrationHoldsWhatTheImportReadOverOtherDefaults()
        {
            Compare(OtherDefaults);
        }

        private static void Compare(string defaultsIni)
        {
            List<DifferentialInput> inputs = Inputs.All().ToList();
            var failures = new ConcurrentBag<string>();
            var deferred = new ConcurrentBag<string>();
            var refused = new ConcurrentBag<string>();
            var created = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
            byte[] committed = File.ReadAllBytes(ConfigTests.Committed());
            Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, input =>
            {
                ImportOutcome import = ImportOutcome.Run(input);
                foreach (bool readOnly in input.Bytes == null ? new[] { false } : new[] { false, true })
                {
                    string name = input.Name + (readOnly ? " (read-only)" : "");
                    MigrationOutcome migration = MigrationOutcome.Run(input, defaultsIni, readOnly);

                    if (import.Error != null || migration.Error != null)
                    {
                        if (import.Error != migration.Error) failures.Add(name + ": import " + import.Error + ", migration " + migration.Error);
                        if (!readOnly) refused.Add(input.Name);
                        continue;
                    }

                    string imported = MigrationOutcome.Describe(import.Config);
                    string migrated = MigrationOutcome.Describe(migration.Config);
                    if (input.Bytes == null)
                    {
                        if (migration.Status != ConfigLoadStatus.Created) failures.Add(name + ": " + migration.Status);
                        if (!migration.Created.SequenceEqual(committed)) failures.Add(name + ": the created file is not config/CameraUnlock.ini");
                        // Every code default is the value the published builds shipped, so without a
                        // file the new CameraUnlock.ini holds what v0.2.0 ran on, as long as
                        // Defaults.ini gives the built-in values.
                        if (defaultsIni == null && imported != migrated) failures.Add(name + ":\n" + Diff(imported, migrated));
                        continue;
                    }

                    if (migration.Status == ConfigLoadStatus.Deferred)
                    {
                        if (!readOnly) deferred.Add(input.Name);
                        if (!migration.Reason.Contains("cannot be converted")) failures.Add(name + ": deferred: " + migration.Reason);
                    }
                    else if (migration.Status != ConfigLoadStatus.Migrated)
                    {
                        failures.Add(name + ": " + migration.Status + ": " + migration.Reason);
                        continue;
                    }
                    else
                    {
                        created[Sha256(migration.Created)] = migration.Created;
                    }
                    if (imported != migrated) failures.Add(name + ":\n" + Diff(imported, migrated));
                }
            });
            Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f, StringComparer.Ordinal).Take(20)));
            // Handed to core's canonical config lint by tests/config_differential/lint-migrated.mjs,
            // which pixi run test runs next.
            foreach (KeyValuePair<string, byte[]> file in created)
            {
                File.WriteAllBytes(Path.Combine(MigratedDir.Value, file.Key + ".ini"), file.Value);
            }
            Assert.Equal(ComparisonOneTests.RefusedByBepInEx(), refused.OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(Deferred(), deferred.OrderBy(n => n, StringComparer.Ordinal));
        }

        /// <summary>
        /// The map proof: on every input, what the converted plugin runs on from the import is what
        /// v0.2.0 ran on, apart from exactly the values the approved changes drop.
        /// </summary>
        [Fact]
        public void TheImportKeepsEverySettingButTheApprovedDrops()
        {
            var failures = new ConcurrentBag<string>();
            Parallel.ForEach(Inputs.All().ToList(), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, input =>
            {
                LegacyOutcome oracle = Oracle.Run(input);
                ImportOutcome import = ImportOutcome.Run(input);
                if (oracle.Error != null)
                {
                    if (import.Error != oracle.Error) failures.Add(input.Name + ": " + import.Error);
                    return;
                }
                LegacyConfig old = oracle.Config;
                ImportResult result = import.Result;
                ImportStatus status = input.Bytes == null ? ImportStatus.Absent : ImportStatus.Imported;
                if (result.Status != status) failures.Add(input.Name + ": " + result.Status);

                SortedDictionary<string, string> before = LegacyStartup.Of(old);
                SortedDictionary<string, string> after = ConvertedStartup.Of(import.Config);
                // A sensitivity group is compared unless one of its values was dropped: the folded
                // values are what the runtime applies now, so they must equal what v0.2.0 applied.
                bool rotationDropped = result.Dropped.Any(d => d.Rule == DropRule.PoseShaping && d.Section == "Sensitivity");
                bool positionDropped = result.Dropped.Any(d => d.Rule == DropRule.PoseShaping && d.Section == "Position");
                bool crosshairDropped = result.Dropped.Any(d => d.Rule == DropRule.Reticle && d.Key == "MoveCrosshair");
                foreach (string key in before.Keys)
                {
                    if (key == "RotationSensitivity" && rotationDropped) continue;
                    if (key == "PositionSensitivity" && positionDropped) continue;
                    if (key == "CrosshairFollowsAim" && crosshairDropped) continue;
                    if (before[key] != after[key]) failures.Add(input.Name + ": " + key + " " + before[key] + " -> " + after[key]);
                }

                var expectedDrops = new List<string>();
                var expectedShaping = new List<string>();
                Action<string, string, float, float> shaping = (section, key, value, shipped) =>
                {
                    bool folded = value == shipped;
                    expectedShaping.Add(section + " " + key + " " + Codec(value) + " " + Codec(shipped) + " " + folded);
                    if (!folded) expectedDrops.Add("PoseShaping " + section + " " + key + " " + Codec(value));
                };
                if (!old.MoveCrosshair) expectedDrops.Add("Reticle UI MoveCrosshair false");
                shaping("Sensitivity", "YawSensitivity", old.YawSensitivity, 1.0f);
                shaping("Sensitivity", "PitchSensitivity", old.PitchSensitivity, 1.0f);
                shaping("Sensitivity", "RollSensitivity", old.RollSensitivity, 1.0f);
                shaping("Position", "PositionSensitivityX", old.PositionSensitivityX, 1.0f);
                shaping("Position", "PositionSensitivityY", old.PositionSensitivityY, 1.0f);
                shaping("Position", "PositionSensitivityZ", old.PositionSensitivityZ, 1.0f);

                string[] drops = result.Dropped.Select(d => d.Rule + " " + d.Section + " " + d.Key + " " + d.Value).ToArray();
                string[] poses = result.PoseShaping.Select(p => p.Section + " " + p.Key + " " + p.Value + " " + p.Shipped + " " + p.Folded).ToArray();
                if (!drops.SequenceEqual(expectedDrops)) failures.Add(input.Name + ": dropped " + string.Join("; ", drops));
                if (!poses.SequenceEqual(expectedShaping)) failures.Add(input.Name + ": pose shaping " + string.Join("; ", poses));
            });
            Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f, StringComparer.Ordinal).Take(20)));
        }

        /// <summary>
        /// Every build's shipped defaults fold: the pose-shaping values of each first-run file are
        /// the ones the conversion moved into code, so nothing is dropped for a player who never
        /// changed them.
        /// </summary>
        [Fact]
        public void EveryFirstRunFileFoldsItsPoseShaping()
        {
            foreach (DifferentialInput input in Inputs.FirstRuns())
            {
                ImportOutcome import = ImportOutcome.Run(input);
                Assert.True(import.Result.PoseShaping.All(p => p.Folded), input.Name);
                Assert.Empty(import.Result.Dropped);
            }
        }

        /// <summary>
        /// Fresh equals upgrade: each published build's first-run file, the Game Pass build's
        /// included, migrates over the built-in Defaults.ini into the committed file byte for byte.
        /// </summary>
        [Fact]
        public void EveryFirstRunMigratesToTheCommittedFile()
        {
            byte[] committed = File.ReadAllBytes(ConfigTests.Committed());
            foreach (DifferentialInput input in Inputs.FirstRuns())
            {
                MigrationOutcome migration = MigrationOutcome.Run(input, null, false);

                Assert.Equal(ConfigLoadStatus.Migrated, migration.Status);
                Assert.Equal(Encoding.ASCII.GetString(committed), Encoding.ASCII.GetString(migration.Created));
                Assert.DoesNotContain(migration.Log, l => l.Contains("MoveCrosshair"));
            }
        }

        /// <summary>Every KeyCode a .cfg can name converts to the key name that reads back as it.</summary>
        [Fact]
        public void EveryUnityKeyCodeConvertsToItsName()
        {
            string keys = File.ReadAllText(Path.Combine(ConfigTests.RepoRoot(), "cameraunlock-core", "data", "keys.json"));
            var codes = new List<int>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(keys, "\"unity\":\\s*(\\d+)"))
            {
                codes.Add(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            Assert.True(codes.Count > 300, "keys.json gave " + codes.Count + " Unity codes");
            foreach (int code in codes.Where(c => c != 0))
            {
                string list = LegacyConfigImport.HotkeyList((UnityEngine.KeyCode)code, UnityEngine.KeyCode.Y);
                KeyBinding[] bindings;
                string error;
                Assert.True(KeyBindings.TryParse(list, out bindings, out error), code + ": " + list + ": " + error);
                Assert.Equal(new KeyBinding(KeyModifiers.None, code), bindings[0]);
                Assert.Equal(new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, (int)UnityEngine.KeyCode.Y), bindings[1]);
            }
            Assert.Equal("Ctrl+Shift+G", LegacyConfigImport.HotkeyList(UnityEngine.KeyCode.None, UnityEngine.KeyCode.G));
        }

        private static string Codec(float value)
        {
            return Encoding.ASCII.GetString(new FloatCodec().Render(value));
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var text = new StringBuilder();
                foreach (byte b in sha.ComputeHash(bytes)) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        private static string Diff(string expected, string actual)
        {
            string[] e = expected.Split('\n');
            string[] a = actual.Split('\n');
            var lines = new List<string>();
            for (int i = 0; i < e.Length && i < a.Length; i++)
            {
                if (e[i] != a[i]) lines.Add("  expected " + e[i] + " | got " + a[i]);
            }
            return string.Join("\n", lines);
        }
    }
}
