﻿using LiveSplit.Model;
using LiveSplit.Model.Input;
using LiveSplit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.Minecraft
{
    public enum MinecraftTimingMethod
    {
        IGT,
        RTA_WITHOUT_LOADS
    }

    public class MinecraftComponent : LiveSplit.UI.Components.IComponent
    {

        public readonly TimerModel timer;
        public readonly MinecraftMemory memory;
        public readonly MinecraftAutosplitter autosplitter;
        public readonly MinecraftSettings settings;

        // Limit the rate at which some operations are done since they are too expensive to run on every udpate()
        private DateTime nextIGTCheck;

        // Avoids checking the setting on every update()
        private bool advancedFeaturesEnabled;

        // Avoids checking the setting on every update()
        public MinecraftTimingMethod TimingMethod { get; private set; }

        private string latestSaveStatsPath;

        public MinecraftComponent(LiveSplitState state)
        {
            timer = new TimerModel() { CurrentState = state };
            timer.CurrentState.IsGameTimePaused = true;

            settings = new MinecraftSettings(this);
            memory = new MinecraftMemory(this);
            autosplitter = new MinecraftAutosplitter(this);

            state.OnStart += OnStart;
            state.OnReset += OnReset;

            advancedFeaturesEnabled = Properties.Settings.Default.AdvancedFeaturesEnabled;
            Properties.Settings.Default.PropertyChanged += OnSettingsChanged;

            if (advancedFeaturesEnabled)
            {
                autosplitter.Setup();
            }
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            // Check the hook on every update
            bool isStillHooked = memory.IsStillHooked();

            // Game time only auto increments between updates when doing rta without loads TODO cache this?
            state.IsGameTimePaused = (TimingMethod != MinecraftTimingMethod.RTA_WITHOUT_LOADS) || memory.IsPaused();

            if (advancedFeaturesEnabled && TimingMethod == MinecraftTimingMethod.IGT && isStillHooked)
            {
                memory.UpdateIGT();
            }

            if (!advancedFeaturesEnabled && ShouldCheckIGT())
            {
                // If the timer is not running yet check if the stats folder exists first to avoid exceptions during world creation
                if (timer.CurrentState.CurrentPhase == TimerPhase.NotRunning && !Directory.Exists(latestSaveStatsPath)) return;

                // Update IGT, it uses the stats.json file since level.dat is considered inaccurate
                var igt = TimeSpan.FromSeconds(ExtractTicks() / 20.0);
                if (timer.CurrentState.CurrentPhase == TimerPhase.Running)
                {
                    // Run in process, update time normally
                    timer.CurrentState.SetGameTime(igt);
                }
                else if (timer.CurrentState.CurrentPhase == TimerPhase.Ended)
                {
                    // Run has ended and IGT has changed, update time with ugly hack
                    timer.CurrentState.CurrentSplitIndex--;
                    var newSplitTime = new Time
                    {
                        RealTime = timer.CurrentState.CurrentSplit.SplitTime.RealTime,
                        GameTime = igt
                    };
                    timer.CurrentState.CurrentSplit.SplitTime = newSplitTime;
                    timer.CurrentState.CurrentSplitIndex++;
                    timer.CurrentState.Run.HasChanged = true;
                }
            }
        }

        private bool ShouldCheckIGT()
        {
            if (nextIGTCheck != null && DateTime.Now < nextIGTCheck)
            {
                // Not yet
                return false;
            }
            else
            {
                // Haven't attempted yet or it's time to do so
                nextIGTCheck = DateTime.Now.AddMilliseconds(1000);
                return true;
            }
        }

        private void FindLatestSaveLevelPath()
        {
            try
            {
                var latestSavePath = new DirectoryInfo(Properties.Settings.Default.SavesPath)
                    .GetDirectories()
                    .OrderByDescending(x => x.LastWriteTime)
                    .First().FullName;

                latestSaveStatsPath = Path.Combine(latestSavePath, "stats");
            }
            catch
            {
                timer.Reset();
                MessageBox.Show("Couldn't find the Minecraft world save.\n\n" +
                    "Check that your saves location is correct on the settings page and that there is already a save to extract the IGT from.",
                    ComponentName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int ExtractTicks()
        {
            var statsFile = Directory.EnumerateFiles(latestSaveStatsPath, "*.json").FirstOrDefault();
            var statsText = File.ReadAllLines(statsFile)[0];
            var statStart = statsText.IndexOf("inute\":") + 7;
            var statEnd = statsText.IndexOf(",", statStart);

            return int.Parse(statsText.Substring(statStart, statEnd - statStart));
        }

        private void OnStart(object sender, EventArgs e)
        {
            if (!advancedFeaturesEnabled)
            {
                FindLatestSaveLevelPath();
            }
        }

        private void OnReset(object sender, TimerPhase phase)
        {
            if (advancedFeaturesEnabled)
            {
                autosplitter.SetupAdvancements();
            }
        }

        public void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AdvancedFeaturesEnabled":
                    advancedFeaturesEnabled = Properties.Settings.Default.AdvancedFeaturesEnabled;
                    timer.Reset();
                    if (advancedFeaturesEnabled)
                    {
                        autosplitter.Setup();
                    }
                    break;
                case "TimingMethod":
                    TimingMethod = (MinecraftTimingMethod)Properties.Settings.Default.TimingMethod;
                    // TODO is this needed?
                    timer.Reset();
                    break;
                case "Advancements":
                    // Set the enabled advancements as the advancement list for the autosplitter
                    autosplitter.SetupAdvancements();
                    break;
                default:
                    break;
            }
        }

        public void Dispose() { }

        public Control GetSettingsControl(LayoutMode mode) => settings;

        // Unused since the settings are stored as .NET user settings
        public XmlNode GetSettings(XmlDocument document) => document.CreateElement("Settings");
        // Unused since the settings are stored as .NET user settings
        public void SetSettings(XmlNode settings) { }

        public string ComponentName => "Minecraft Component";

        public IDictionary<string, Action> ContextMenuControls { get; }

        // We take up no space visually, so we return nothing/zero for visual calls from LiveSplit
        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion) { }
        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) { }
        public float HorizontalWidth { get { return 0; } }
        public float MinimumWidth { get { return 0; } }
        public float VerticalHeight { get { return 0; } }
        public float MinimumHeight { get { return 0; } }
        public float PaddingBottom { get { return 0; } }
        public float PaddingLeft { get { return 0; } }
        public float PaddingRight { get { return 0; } }
        public float PaddingTop { get { return 0; } }

    }
}
