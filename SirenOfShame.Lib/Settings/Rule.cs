﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SirenOfShame.Lib.Device;
using SirenOfShame.Lib.Watcher;

namespace SirenOfShame.Lib.Settings
{
    public enum TriggerType
    {
        BuildTriggered = 0,
        InitialFailedBuild = 1,
        SubsequentFailedBuild = 2,
        SuccessfulBuild = 3,
        InitialSuccess = 4,
        BuildFailed = 5
    }

    public enum AlertType
    {
        Inherit = 0,
        NoAlert = 1,
        ModalDialog = 2,
        TrayAlert = 3,
    }

    [Serializable]
    public class Rule
    {
        public static IDictionary<TriggerType, string> TriggerTypes = new Dictionary<TriggerType, string>
        {
            { TriggerType.BuildTriggered, "A build starts" },
            { TriggerType.BuildFailed, "A build fails" },
            { TriggerType.InitialFailedBuild, "A build fails - for the first time" },
            { TriggerType.SubsequentFailedBuild, "A build fails - any subsequent time" },
            { TriggerType.SuccessfulBuild, "A build succeeds" },
            { TriggerType.InitialSuccess, "A build succeeds - first time after failure" },
        };

        /// <summary>
        /// null = any build
        /// </summary>
        public string BuildDefinitionId { get; set; }

        /// <summary>
        /// null = "anyone else"
        /// </summary>
        public string TriggerPerson { get; set; }

        public TriggerType TriggerType { get; set; }

        public AlertType AlertType { get; set; }

        public string AlertTypeDescription { get
        {
            switch (AlertType)
            {
                case AlertType.Inherit:
                    return "Inherit";
                case AlertType.ModalDialog:
                    return "Modal Dialog";
                case AlertType.NoAlert:
                    return "No Alert";
                case AlertType.TrayAlert:
                    return "Tray Alert";
                default:
                    throw new Exception("Unknown alert type " + AlertType);
            }
        } }

        /// <summary>
        /// null = until the build passes
        /// </summary>
        public int? AudioDuration { get; set; }
        
        public bool PlayAudioUntilBuildPasses
        {
            get { return AudioDuration == null; }
        }

        /// <summary>
        /// null = until the build passes
        /// </summary>
        public int? LightsDuration { get; set; }

        public bool PlayLightsUntilBuildPasses
        {
            get { return LightsDuration == null; }
        }

        public bool InheritAudioSettings { get; set; }
        public bool InheritLedSettings { get; set; }

        /// <summary>
        /// null = inherit (in other words keep playing if you were playing)
        /// </summary>
        public AudioPattern AudioPattern { get; set; }

        public bool PlayAudio
        {
            get { return AudioPattern != null; }
        }

        /// <summary>
        /// null = inherit (in other words keep playing if you were playing)
        /// </summary>
        public LedPattern LedPattern { get; set; }

        public bool PlayLights
        {
            get { return LedPattern != null; }
        }

        public string TriggerTypeDescription { 
            get { return TriggerTypes[TriggerType]; } 
        }
        
        public string TriggerPersonDescription {
            get { return TriggerPerson ?? "Anyone"; }
        }

        /// <summary>
        /// Higher priority numbers should be run first
        /// </summary>
        public int PriorityId
        {
            get
            {
                int priority = 0;
                if (TriggerPerson != null) priority = priority + 4;
                if (BuildDefinitionId != null) priority = priority + 2;
                if (TriggerType == TriggerType.InitialFailedBuild) priority = priority + 1;
                if (TriggerType == TriggerType.InitialSuccess) priority = priority + 1;

                return priority;
            }
        }

        public ListViewItem AsListViewItem()
        {
            var listViewItem = new ListViewItem(TriggerTypeDescription);
            listViewItem.SubItems.Add(BuildDefinitionId ?? "Any");
            listViewItem.SubItems.Add(TriggerPersonDescription);
            listViewItem.SubItems.Add(AlertTypeDescription);
            listViewItem.Tag = this;
            return listViewItem;
        }

        public bool IsMatch(BuildStatus buildStatus, BuildStatusEnum? previousStatus)
        {
            bool isBuildDefinitionMatch = BuildDefinitionId == null || BuildDefinitionId == buildStatus.Id;
            if (!isBuildDefinitionMatch) return false;
            bool isPersonMatch = string.IsNullOrEmpty(TriggerPerson) || TriggerPerson == buildStatus.RequestedBy;
            if (!isPersonMatch) return false;
            
            var newlyBroken = buildStatus.IsNewlyBroken(previousStatus);
            var newlyFixed = buildStatus.IsNewlyFixed(previousStatus);
            bool isTriggerTypeMatch = IsTriggerTypeMatch(buildStatus, newlyBroken, newlyFixed);
            return isTriggerTypeMatch;
        }

        private bool IsTriggerTypeMatch(BuildStatus buildStatus, bool newlyBroken, bool newlyFixed)
        {
            if (TriggerType == TriggerType.BuildFailed && buildStatus.BuildStatusEnum == BuildStatusEnum.Broken) return true;
            if (TriggerType == TriggerType.InitialFailedBuild       && newlyBroken) return true;
            if (TriggerType == TriggerType.InitialSuccess           && newlyFixed) return true;
            if (TriggerType == TriggerType.BuildTriggered           && buildStatus.BuildStatusEnum == BuildStatusEnum.InProgress) return true;
            if (TriggerType == TriggerType.SubsequentFailedBuild    && buildStatus.BuildStatusEnum == BuildStatusEnum.Broken && !newlyBroken) return true;
            if (TriggerType == TriggerType.SuccessfulBuild          && buildStatus.BuildStatusEnum == BuildStatusEnum.Working && !newlyFixed) return true;
            return false;
        }

        public void FireAnyUntilBuildPassesEvents(RulesEngine rulesEngine, BuildStatus buildStatus, BuildStatusEnum? previousStatus)
        {
            var newlyFixed = buildStatus.IsNewlyFixed(previousStatus);
            if (PlayAudio && PlayAudioUntilBuildPasses && newlyFixed)
            {
                rulesEngine.InvokeSetAudio(null, null);
            }
            if (PlayLights && PlayLightsUntilBuildPasses && newlyFixed)
            {
                rulesEngine.InvokeSetLights(null, null);
            }
        }

        public void FireEvent(RulesEngine rulesEngine, BuildStatusEnum? previousStatus, BuildStatus buildStatus)
        {
            var newlyBroken = buildStatus.IsNewlyBroken(previousStatus);
            var newlyFixed = buildStatus.IsNewlyFixed(previousStatus);

            string message = null;
            if (newlyBroken)
                message = "Build newly broken by " + buildStatus.RequestedBy;
            if (newlyFixed)
                message = "Build is passing again";
            if (buildStatus.BuildStatusEnum == BuildStatusEnum.InProgress)
                message = "Build triggered by " + buildStatus.RequestedBy;
            if (buildStatus.BuildStatusEnum == BuildStatusEnum.Broken && !newlyBroken)
                message = "Build still broken";
            if (buildStatus.BuildStatusEnum == BuildStatusEnum.Working && !newlyFixed)
                message = "Build passed";
            message += " for " + buildStatus.Name;

            if (AlertType == AlertType.TrayAlert)
            {
                rulesEngine.InvokeTrayNotify(new TrayNotifyEventArgs
                {
                    TipIcon = buildStatus.BuildStatusEnum == BuildStatusEnum.Broken ? ToolTipIcon.Error : ToolTipIcon.Info,
                    Title = string.Format("Build {0}", buildStatus.BuildStatusDescription),
                    TipText = message,
                });
            }

            if (LedPattern != null)
            {
                rulesEngine.InvokeSetLights(LedPattern, LightsDuration);
            }
            
            if (AudioPattern != null)
            {
                rulesEngine.InvokeSetAudio(AudioPattern, AudioDuration);
            }
            if (AlertType == AlertType.ModalDialog)
            {
                rulesEngine.InvokeModalDialog(message);
            }
        }
    }
}