﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using log4net;
using SirenOfShame.Lib.Device;
using SirenOfShame.Lib.Settings;
using SirenOfShame.Lib.Util;
using Timer = System.Windows.Forms.Timer;

namespace SirenOfShame.Lib.Watcher
{
    public class RulesEngine
    {
        private static readonly ILog _log = MyLogManager.GetLogger(typeof(RulesEngine));
        protected IDictionary<string, BuildStatus> PreviousWorkingOrBrokenBuildStatus { get; set; }

        private readonly SirenOfShameSettings _settings;
        private WatcherBase _watcher;

        public event UpdateStatusBarEvent UpdateStatusBar;
        public event StatusChangedEvent RefreshStatus;
        public event TrayNotifyEvent TrayNotify;
        public event ModalDialogEvent ModalDialog;
        public event SetAudioEvent SetAudio;
        public event SetLightsEvent SetLights;
        public event SetTrayIconEvent SetTrayIcon;

        public void InvokeSetTrayIcon(TrayIcon trayIcon)
        {
            SetTrayIconEvent setTrayIcon = SetTrayIcon;
            if (setTrayIcon != null) setTrayIcon(this, new SetTrayIconEventArgs { TrayIcon = trayIcon });
        }

        public void InvokeTrayNotify(TrayNotifyEventArgs args)
        {
            TrayNotifyEvent handler = TrayNotify;
            if (handler != null) handler(this, args);
        }

        public RulesEngine(SirenOfShameSettings settings)
        {
            ResetPreviousWorkingOrBrokenStatuses();
            _settings = settings;
            _timer.Interval = 1000;
            _timer.Tick += TimerTick;
        }

        private bool _serverPreviouslyUnavailable;

        private void BuildWatcherServerUnavailable(object sender, ServerUnavailableEventArgs args)
        {
            InvokeUpdateStatusBar("Build server unavailable, attempting to reconnect");
            SetStatusUnknown();
            // only notify that it was unavailable once
            if (_serverPreviouslyUnavailable)
            {
                return;
            }

            InvokeTrayNotify(new TrayNotifyEventArgs
            {
                Title = "Build Server Unavailable",
                TipText = args.Exception.Message,
                TipIcon = ToolTipIcon.Error
            });
            ResetPreviousWorkingOrBrokenStatuses();
            _serverPreviouslyUnavailable = true;
        }

        private void ResetPreviousWorkingOrBrokenStatuses()
        {
            PreviousWorkingOrBrokenBuildStatus = new Dictionary<string, BuildStatus>();
            _buildStatus = new BuildStatus[] {};
        }

        private BuildStatus[] _buildStatus = new BuildStatus[] { };

        private void InvokeUpdateStatusBar(string statusText)
        {
            var datedStatusText = string.Format("{0:G} - {1}", DateTime.Now, statusText);
            var updateStatusBar = UpdateStatusBar;
            if (updateStatusBar == null) return;
            updateStatusBar(this, new UpdateStatusBarEventArgs { StatusText = datedStatusText });
        }

        private void BuildWatcherStatusChecked(object sender, StatusCheckedEventArgsArgs args)
        {
            if (_serverPreviouslyUnavailable)
            {
                InvokeTrayNotify(new TrayNotifyEventArgs { Title = "Reconnected", TipText = "Reconnected to server.", TipIcon = ToolTipIcon.Info });
            }
            _serverPreviouslyUnavailable = false;

            var newBuildStatus = BuildStatusUtil.Merge(_buildStatus, args.BuildStatuses);
            var oldBuildStatus = _buildStatus;
            _buildStatus = newBuildStatus;

            InvokeUpdateStatusBar("Connected");

            // e.g. if a build exists in newStatus but doesn't exit in oldStatus, return it.  If a build exists in
            //  oldStatus and in newStatus and the BuildStatusEnum is different then return it.
            var changedBuildStatuses = from newStatus in newBuildStatus
                                       from oldStatus in oldBuildStatus.Where(s => s.Id == newStatus.Id).DefaultIfEmpty()
                                       where oldStatus == null || (oldStatus.BuildStatusEnum != newStatus.BuildStatusEnum)
                                       select newStatus;
            changedBuildStatuses = changedBuildStatuses.ToList();

            BuildWatcherStatusChanged(newBuildStatus, changedBuildStatuses);
        }

        private void InvokeRefreshStatus(IEnumerable<BuildStatus> buildStatuses)
        {
            IEnumerable<BuildStatusListViewItem> buildStatusListViewItems = buildStatuses
                .OrderBy(s => s.Name)
                .Select(bs => bs.AsBuildStatusListViewItem(DateTime.Now, PreviousWorkingOrBrokenBuildStatus));

            var refreshStatus = RefreshStatus;
            if (refreshStatus == null) return;
            refreshStatus(this, new RefreshStatusEventArgs { BuildStatusListViewItems = buildStatusListViewItems });
        }

        private void TimerTick(object sender, EventArgs e)
        {
            if (_buildStatus.Any(bs => bs.BuildStatusEnum == BuildStatusEnum.InProgress))
            {
                _log.Debug("Tick: some build is in progress");
                InvokeRefreshStatus(_buildStatus);
            }
        }

        private void BuildWatcherStatusChanged(IEnumerable<BuildStatus> allBuildStatuses, IEnumerable<BuildStatus> changedBuildStatuses)
        {
            Debug.Assert(changedBuildStatuses != null, "changedBuildStatuses should not be null");
            Debug.Assert(PreviousWorkingOrBrokenBuildStatus != null, "PreviousWorkingOrBrokenBuildStatus should never be null");

            if (changedBuildStatuses.Any())
            {
                _log.Debug("InvokeRefreshStatus: Some build status changed");
                InvokeRefreshStatus(allBuildStatuses);
            }

            InvokeSetTrayIconForChangedBuildStatuses(allBuildStatuses);
            AddRequestedByPersonToBuildStatusSettings(changedBuildStatuses);

            foreach (var changedBuildStatus in changedBuildStatuses)
            {
                BuildStatus previousWorkingOrBrokenBuildStatus;
                PreviousWorkingOrBrokenBuildStatus.TryGetValue(changedBuildStatus.Id, out previousWorkingOrBrokenBuildStatus);

                BuildStatusEnum? previousStatus = previousWorkingOrBrokenBuildStatus == null ? (BuildStatusEnum?)null : previousWorkingOrBrokenBuildStatus.BuildStatusEnum;
                changedBuildStatus.Changed(previousStatus, this, _settings.Rules);

                if (changedBuildStatus.IsWorkingOrBroken())
                {
                    BuildStatus status;
                    bool exists = PreviousWorkingOrBrokenBuildStatus.TryGetValue(changedBuildStatus.Id, out status);
                    if (!exists)
                    {
                        PreviousWorkingOrBrokenBuildStatus.Add(changedBuildStatus.Id, changedBuildStatus);
                    }
                    else
                    {
                        PreviousWorkingOrBrokenBuildStatus[changedBuildStatus.Id] = changedBuildStatus;
                    }
                }
            }
        }

        private void InvokeSetTrayIconForChangedBuildStatuses(IEnumerable<BuildStatus> allBuildStatuses)
        {
            var buildStatusesAndSettings = from buildStatus in allBuildStatuses
                                           join setting in _settings.BuildDefinitionSettings on buildStatus.Id
                                               equals setting.Id
                                           select new { buildStatus, setting };
            bool anyBuildBroken = buildStatusesAndSettings
                .Any(bs => bs.setting.AffectsTrayIcon && bs.buildStatus.BuildStatusEnum == BuildStatusEnum.Broken);
            TrayIcon trayIcon = anyBuildBroken ? TrayIcon.Red : TrayIcon.Green;
            InvokeSetTrayIcon(trayIcon);
        }

        private void AddRequestedByPersonToBuildStatusSettings(IEnumerable<BuildStatus> changedBuildStatuses)
        {
            var buildStatusesWithNewPeople = from buildStatus in changedBuildStatuses
                                             join setting in _settings.BuildDefinitionSettings on buildStatus.Id equals setting.Id
                                             where !setting.ContainsPerson(buildStatus)
                                             select new { buildStatus, setting };

            var buildsWithoutRequestedByPerson = buildStatusesWithNewPeople.ToList();
            buildsWithoutRequestedByPerson.ForEach(bss => bss.setting.People.Add(bss.buildStatus.RequestedBy));
            if (buildsWithoutRequestedByPerson.Any())
                _settings.Save();
        }

        internal void InvokeModalDialog(string dialogText)
        {
            var modalDialog = ModalDialog;
            if (modalDialog == null) return;
            modalDialog(this, new ModalDialogEventArgs { DialogText = dialogText });
        }

        public void InvokeSetAudio(AudioPattern audioPattern, int? duration)
        {
            var startSiren = SetAudio;
            if (startSiren == null) return;
            startSiren(this, new SetAudioEventArgs { AudioPattern = audioPattern, Duration = duration });
        }

        public void InvokeSetLights(LedPattern ledPattern, int? duration)
        {
            var stopSiren = SetLights;
            if (stopSiren == null) return;
            stopSiren(this, new SetLightsEventArgs { LedPattern = ledPattern, Duration = duration });
        }

        private Thread _watcherThread;

        Timer _timer = new Timer();

        public void Start()
        {
            _watcher = _settings.GetWatcher();
            
            if (_watcher != null)
            {
                InvokeUpdateStatusBar("Attempting to connect to server");
                SetStatusUnknown();

                _watcher.StatusChecked += BuildWatcherStatusChecked;
                _watcher.ServerUnavailable += BuildWatcherServerUnavailable;
                _watcher.Settings = _settings;
                _watcherThread = new Thread(_watcher.StartWatching) { IsBackground = true };
                _watcherThread.Start();
            }
            _timer.Start();
        }

        private void SetStatusUnknown()
        {
            InvokeSetTrayIcon(TrayIcon.Question);
            InvokeRefreshStatus(_settings.BuildDefinitionSettings.Where(bd => bd.Active).Select(bd => bd.AsUnknownBuildStatus()));
        }

        public void Stop()
        {
            _timer.Stop();
            if (_watcherThread != null)
                _watcherThread.Abort();
        }
    }
}