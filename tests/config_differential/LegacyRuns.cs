using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx.Configuration;
using CameraUnlock.Core.Config.Testing;
using CameraUnlock.Core.Input;
using SuperliminalHeadTracking.Config;
using SuperliminalHeadTracking.Legacy;
using UnityEngine;

namespace SuperliminalHeadTracking.Tests.Differential
{
    /// <summary>One differential input: a legacy file's bytes, or no file at all.</summary>
    internal sealed class DifferentialInput
    {
        public DifferentialInput(string name, byte[] bytes)
        {
            Name = name;
            Bytes = bytes;
        }

        public string Name { get; }

        /// <summary>Null for no file.</summary>
        public byte[] Bytes { get; }
    }

    internal static class Inputs
    {
        public const string LegacyName = "com.cameraunlock.superliminal.headtracking.cfg";

        public static string RepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pixi.toml"))) dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("no pixi.toml above " + AppDomain.CurrentDomain.BaseDirectory);
            return dir.FullName;
        }

        public static string DataDir()
        {
            return Path.Combine(RepoRoot(), "tests", "config_differential", "data");
        }

        public static string FirstRunDir()
        {
            return Path.Combine(DataDir(), "first-run");
        }

        /// <summary>The newest published Steam build's first-run file, the base the corpus mutates.</summary>
        public static byte[] NewestFirstRun()
        {
            return File.ReadAllBytes(Path.Combine(FirstRunDir(), "v0.2.0.cfg"));
        }

        /// <summary>
        /// Every published build's first-run file: v0.1.0 and v0.2.0 as the Steam build writes them
        /// under BepInEx 5, and v0.2.0's Xbox Game Pass build under BepInEx 6.
        /// </summary>
        public static IEnumerable<DifferentialInput> FirstRuns()
        {
            string[] files = Directory.GetFiles(FirstRunDir(), "*.cfg");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length != 3) throw new InvalidOperationException(FirstRunDir() + " holds " + files.Length + " first-run files, not 3");
            foreach (string file in files)
            {
                yield return new DifferentialInput("first run " + Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file));
            }
        }

        public static IEnumerable<DifferentialInput> Corpus()
        {
            foreach (IniMutation m in IniMutations.Generate(NewestFirstRun(), LegacyConfigKeys.All(), Descriptors()))
            {
                yield return new DifferentialInput("corpus " + m.Name, m.Bytes);
            }
        }

        public static IEnumerable<DifferentialInput> All()
        {
            yield return new DifferentialInput("no file", null);
            yield return new DifferentialInput("empty file", new byte[0]);
            foreach (DifferentialInput input in FirstRuns()) yield return input;
            foreach (DifferentialInput input in Corpus()) yield return input;
        }

        /// <summary>
        /// A descriptor per key the reader reads, in its order: another valid value, and a value
        /// beyond each end of every AcceptableValueRange, which BepInEx clamps.
        /// </summary>
        public static List<MutationKey> Descriptors()
        {
            var none = new string[0];
            var noChords = new ChordSwitch[0];
            Func<string, string, string, string[], MutationKey> plain =
                (section, key, alternate, outOfRange) => new MutationKey(section, key, alternate, outOfRange, false, noChords);
            Func<string, string, string, MutationKey> hotkey =
                (section, key, alternate) => new MutationKey(section, key, alternate, none, true, noChords);
            return new List<MutationKey>
            {
                plain("General", "EnabledOnStartup", "false", none),
                plain("General", "ShowStartupNotification", "false", none),
                plain("General", "WorldSpaceYaw", "false", none),
                plain("UI", "ShowConnectionNotifications", "false", none),
                plain("UI", "MoveCrosshair", "false", none),
                hotkey("Keybindings", "ToggleKey", "F9"),
                hotkey("Keybindings", "CycleTrackingModeKey", "F11"),
                hotkey("Keybindings", "YawModeKey", "F12"),
                plain("Network", "UDPPort", "4343", new[] { "80", "70000" }),
                plain("Sensitivity", "YawSensitivity", "1.5", new[] { "0.05", "3.5" }),
                plain("Sensitivity", "PitchSensitivity", "1.5", new[] { "0.05", "3.5" }),
                plain("Sensitivity", "RollSensitivity", "0.5", new[] { "0.05", "3.5" }),
                plain("Smoothing", "LocalSmoothing", "0.3", new[] { "-0.1", "1.5" }),
                plain("Smoothing", "RemoteSmoothing", "0.5", new[] { "-0.1", "1.5" }),
                plain("Position", "PositionEnabled", "false", none),
                plain("Position", "PositionSensitivityX", "1.5", new[] { "-1", "5.5" }),
                plain("Position", "PositionSensitivityY", "1.5", new[] { "-1", "5.5" }),
                plain("Position", "PositionSensitivityZ", "1.5", new[] { "-1", "5.5" }),
                plain("Position", "PositionLimitX", "0.25", new[] { "0.001", "0.6" }),
                plain("Position", "PositionLimitY", "0.15", new[] { "0.001", "0.6" }),
                plain("Position", "PositionLimitZ", "0.35", new[] { "0.001", "0.6" }),
                plain("Position", "PositionLimitZBack", "0.05", new[] { "0.001", "0.6" }),
                plain("Position", "TrackerPivotForward", "0.08", new[] { "-0.1", "0.3" }),
                plain("Collision", "CollisionEnabled", "false", none),
                plain("Collision", "CollisionMargin", "0.25", new[] { "0.01", "0.7" }),
                plain("Collision", "CollisionReleaseSmoothing", "0.5", new[] { "-0.1", "1.5" }),
                plain("Diagnostics", "LogAimGeometry", "true", none),
            };
        }
    }

    /// <summary>
    /// A scratch folder holding at most the legacy file. <see cref="With{T}"/> deletes it only once
    /// the run inside has returned, so an exception from the run reaches the test as it was thrown,
    /// and the folder it failed in is left behind to look at.
    /// </summary>
    internal sealed class LegacyFolder
    {
        private LegacyFolder(DifferentialInput input)
        {
            Path = Scratch.Create("sl-diff-");
            LegacyPath = System.IO.Path.Combine(Path, Inputs.LegacyName);
            if (input.Bytes != null) File.WriteAllBytes(LegacyPath, input.Bytes);
        }

        public static T With<T>(DifferentialInput input, Func<LegacyFolder, T> run)
        {
            var folder = new LegacyFolder(input);
            T result = run(folder);
            Scratch.Delete(folder.Path);
            return result;
        }

        public string Path { get; }

        public string LegacyPath { get; }

        public string[] Entries()
        {
            string[] names = Directory.GetFileSystemEntries(Path).Select(System.IO.Path.GetFileName).ToArray();
            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }
    }

    internal static class Scratch
    {
        private const int DeleteAttempts = 20;

        public static string Create(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Windows can hold a file open for a moment after the process that wrote it closed it, a
        /// virus scanner reading it being the usual reason, and a folder delete then fails with a
        /// sharing violation or on a part of the path already gone. Thousands of runs in parallel
        /// hit that often enough to fail a build, so the delete is tried again, for about five
        /// seconds, until the folder is gone.
        /// </summary>
        public static void Delete(string path)
        {
            for (int attempt = 1; Directory.Exists(path); attempt++)
            {
                try
                {
                    foreach (string file in Directory.GetFiles(path)) File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(path, true);
                }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < DeleteAttempts)
                {
                    Thread.Sleep(attempt * 25);
                }
            }
        }
    }

    /// <summary>
    /// What one run of a legacy reader gave: the settings, or the exception BepInEx threw reading
    /// the file, which in game stops the plugin loading at all.
    /// </summary>
    internal sealed class LegacyOutcome
    {
        public LegacyConfig Config;
        public bool Found;
        public string Error;

        public string Describe()
        {
            if (Error != null) return "throws " + Error;
            return "found=" + LegacyStartup.Text(Found) + "\n" + LegacyStartup.Fields(Config);
        }

        public static string ErrorOf(Exception e)
        {
            return e.GetType().FullName + ": " + e.Message;
        }
    }

    /// <summary>
    /// What v0.2.0 ran on for one input, read by its own ConfigManager on a ConfigFile built as
    /// BepInEx's BaseUnityPlugin builds the plugin's, which reads an existing file at once.
    /// </summary>
    internal static class Oracle
    {
        public static LegacyOutcome Run(DifferentialInput input)
        {
            return LegacyFolder.With(input, folder =>
            {
                ConfigManager manager;
                try
                {
                    var file = new ConfigFile(folder.LegacyPath, false);
                    manager = new ConfigManager();
                    manager.Initialize(file);
                }
                catch (ArgumentException e)
                {
                    return new LegacyOutcome { Error = LegacyOutcome.ErrorOf(e) };
                }
                return new LegacyOutcome
                {
                    Found = input.Bytes != null,
                    Config = new LegacyConfig
                    {
                        EnabledOnStartup = manager.EnabledOnStartup.Value,
                        ShowStartupNotification = manager.ShowStartupNotification.Value,
                        WorldSpaceYaw = manager.WorldSpaceYaw.Value,
                        ShowConnectionNotifications = manager.ShowConnectionNotifications.Value,
                        MoveCrosshair = manager.MoveCrosshair.Value,
                        ToggleKey = manager.ToggleKey.Value,
                        CycleTrackingModeKey = manager.CycleTrackingModeKey.Value,
                        YawModeKey = manager.YawModeKey.Value,
                        UDPPort = manager.UDPPort.Value,
                        YawSensitivity = manager.YawSensitivity.Value,
                        PitchSensitivity = manager.PitchSensitivity.Value,
                        RollSensitivity = manager.RollSensitivity.Value,
                        LocalSmoothing = manager.LocalSmoothing.Value,
                        RemoteSmoothing = manager.RemoteSmoothing.Value,
                        PositionEnabled = manager.PositionEnabled.Value,
                        PositionSensitivityX = manager.PositionSensitivityX.Value,
                        PositionSensitivityY = manager.PositionSensitivityY.Value,
                        PositionSensitivityZ = manager.PositionSensitivityZ.Value,
                        PositionLimitX = manager.PositionLimitX.Value,
                        PositionLimitY = manager.PositionLimitY.Value,
                        PositionLimitZ = manager.PositionLimitZ.Value,
                        PositionLimitZBack = manager.PositionLimitZBack.Value,
                        TrackerPivotForward = manager.TrackerPivotForward.Value,
                        CollisionEnabled = manager.CollisionEnabled.Value,
                        CollisionMargin = manager.CollisionMargin.Value,
                        CollisionReleaseSmoothing = manager.CollisionReleaseSmoothing.Value,
                        LogAimGeometry = manager.LogAimGeometry.Value,
                    },
                };
            });
        }
    }

    /// <summary>
    /// The frozen reader on one input, on a ConfigFile built as the plugin's is. It must leave the
    /// file and its folder as they were.
    /// </summary>
    internal static class FrozenReader
    {
        public static LegacyOutcome Run(DifferentialInput input)
        {
            return LegacyFolder.With(input, folder =>
            {
                string[] before = folder.Entries();
                DateTime written = input.Bytes == null ? DateTime.MinValue : File.GetLastWriteTimeUtc(folder.LegacyPath);

                var outcome = new LegacyOutcome();
                try
                {
                    outcome.Config = LegacyConfigReader.Read(new ConfigFile(folder.LegacyPath, false), out outcome.Found);
                }
                catch (ArgumentException e)
                {
                    outcome.Error = LegacyOutcome.ErrorOf(e);
                }

                if (!before.SequenceEqual(folder.Entries()))
                    throw new InvalidOperationException(input.Name + ": the frozen reader changed the folder: " + string.Join(", ", folder.Entries()));
                if (input.Bytes != null)
                {
                    if (!File.ReadAllBytes(folder.LegacyPath).SequenceEqual(input.Bytes))
                        throw new InvalidOperationException(input.Name + ": the frozen reader rewrote the legacy file");
                    if (File.GetLastWriteTimeUtc(folder.LegacyPath) != written)
                        throw new InvalidOperationException(input.Name + ": the frozen reader touched the legacy file");
                }
                return outcome;
            });
        }
    }

    /// <summary>
    /// What v0.2.0's HeadTrackingPlugin.Awake and InputHandler set up from its settings, one line
    /// per item, floats with their bits.
    /// </summary>
    internal static class LegacyStartup
    {
        public static SortedDictionary<string, string> Of(LegacyConfig c)
        {
            var s = new SortedDictionary<string, string>(StringComparer.Ordinal);
            s["TrackingEnabled"] = Text(c.EnabledOnStartup);
            s["RotationEnabled"] = "true";
            s["PositionEnabled"] = Text(c.PositionEnabled);
            s["WorldSpaceYaw"] = Text(c.WorldSpaceYaw);
            s["UdpPort"] = c.UDPPort.ToString(CultureInfo.InvariantCulture);
            s["ShowStartupNotification"] = Text(c.ShowStartupNotification);
            s["ShowConnectionNotifications"] = Text(c.ShowConnectionNotifications);
            s["CrosshairFollowsAim"] = Text(c.MoveCrosshair);
            s["LocalSmoothing"] = Text(c.LocalSmoothing);
            s["RemoteSmoothing"] = Text(c.RemoteSmoothing);
            s["RotationSensitivity"] = Text(c.YawSensitivity) + " " + Text(c.PitchSensitivity) + " " + Text(c.RollSensitivity);
            s["PositionSensitivity"] = Text(c.PositionSensitivityX) + " " + Text(c.PositionSensitivityY) + " " + Text(c.PositionSensitivityZ);
            s["PositionLimits"] = Text(c.PositionLimitX) + " " + Text(c.PositionLimitY) + " " + Text(c.PositionLimitY)
                                  + " " + Text(c.PositionLimitZ) + " " + Text(c.PositionLimitZBack);
            s["TrackerPivotForward"] = Text(c.TrackerPivotForward);
            s["CollisionEnabled"] = Text(c.CollisionEnabled);
            s["CollisionMargin"] = Text(c.CollisionMargin);
            s["CollisionReleaseSmoothing"] = Text(c.CollisionReleaseSmoothing);
            s["LogAimGeometry"] = Text(c.LogAimGeometry);
            s["ToggleKey"] = Hotkey(c.ToggleKey, KeyCode.Y);
            s["CycleTrackingModeKey"] = Hotkey(c.CycleTrackingModeKey, KeyCode.G);
            s["YawModeKey"] = Hotkey(c.YawModeKey, KeyCode.H);
            return s;
        }

        /// <summary>Every field, floats with their bits.</summary>
        public static string Fields(LegacyConfig c)
        {
            var text = new StringBuilder();
            foreach (FieldInfo field in typeof(LegacyConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = field.GetValue(c);
                string shown = value is float ? Text((float)value)
                    : value is KeyCode ? ((int)(KeyCode)value).ToString(CultureInfo.InvariantCulture) + " " + value
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
                text.Append(field.Name).Append('=').Append(shown).Append('\n');
            }
            return text.ToString();
        }

        public static string Text(bool value)
        {
            return value ? "true" : "false";
        }

        public static string Text(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture) + "/0x"
                   + BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("X8", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The bindings ChordHotkeys.IsActionPressed(primary, letter) fired on: the primary key
        /// whatever else was held, unless it was None, and Ctrl+Shift+letter.
        /// </summary>
        public static string Hotkey(KeyCode primary, KeyCode chordLetter)
        {
            string chord = KeyBindings.Format(new[] { new KeyBinding(KeyModifiers.Ctrl | KeyModifiers.Shift, (int)chordLetter) });
            if (primary == KeyCode.None) return chord;
            return KeyName((int)primary) + ", " + chord;
        }

        /// <summary>A Unity key code's name, or the number for one that names no key.</summary>
        public static string KeyName(int unityKeyCode)
        {
            try
            {
                return KeyBindings.Format(new[] { new KeyBinding(KeyModifiers.None, unityKeyCode) });
            }
            catch (ArgumentException)
            {
                return unityKeyCode.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
