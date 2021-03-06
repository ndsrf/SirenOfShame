﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using log4net;
using Microsoft.TeamFoundation;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.VisualStudio.Services.Common;
using SirenOfShame.Lib;
using SirenOfShame.Lib.Exceptions;
using SirenOfShame.Lib.Settings;
using WindowsCredential = Microsoft.VisualStudio.Services.Common.WindowsCredential;

namespace TfsServices.Configuration
{
    public class MyTfsServer : IDisposable
    {
        private static readonly ILog _log = MyLogManager.GetLogger(typeof(MyTfsServer));
        
        private readonly TfsConfigurationServer _tfsConfigurationServer;
        private NetworkCredential _networkCredential;

        public MyTfsServer(CiEntryPointSetting ciEntryPointSetting)
        {
            try {
                _tfsConfigurationServer = GetTfsConfigurationServer(ciEntryPointSetting.Url, ciEntryPointSetting.UserName, ciEntryPointSetting.GetPassword());
                _tfsConfigurationServer.EnsureAuthenticated();
            } catch (TeamFoundationServiceUnavailableException ex) {
                _log.Debug(ex);
                throw new ServerUnavailableException(ex.Message, ex);
            }
        }

        private TfsConfigurationServer GetTfsConfigurationServer(string url, string rawUserName, string password)
        {
            var uri = new Uri(url);
            if (string.IsNullOrEmpty(rawUserName))
            {
                return new TfsConfigurationServer(uri);
            }

            var usernameParts = rawUserName.Split('\\');
            string userName = usernameParts.LastOrDefault();
            string domain = usernameParts.FirstOrDefault();
            _networkCredential = new NetworkCredential(userName, password, domain);
            return new TfsConfigurationServer(uri, _networkCredential);
        }

        public IEnumerable<MyTfsProjectCollection> ProjectCollections
        {
            get
            {
                try
                {
                    CatalogNode configurationServerNode = _tfsConfigurationServer.CatalogNode;
                    ReadOnlyCollection<CatalogNode> tcpNodes = configurationServerNode.QueryChildren(
                        new[] {CatalogResourceTypes.ProjectCollection},
                        false,
                        CatalogQueryOptions.None
                        );

                    return tcpNodes
                        .Select(tcpNode => new MyTfsProjectCollection(this, tcpNode))
                        .Where(i => i.CurrentUserHasAccess);
                } 
                catch (Exception ex)
                {
                    _log.Error("Unable to retrieve project collections", ex);
                    MessageBox.Show("Unable to retrieve project collections. " + ex.Message);
                    return Enumerable.Empty<MyTfsProjectCollection>();
                }
            }
        }

        public static bool CheckIfIsHostedTfs(string url)
        {
            return url.Contains(".visualstudio.com/");
        }

        public bool IsHostedTfs
        {
            get { return CheckIfIsHostedTfs(_tfsConfigurationServer.Uri.ToString()); }
        }

        public void Dispose()
        {
            if (_tfsConfigurationServer != null)
            {
                _tfsConfigurationServer.Dispose();
            }
        }

        public ILocationService GetConfigLocationService()
        {
            return _tfsConfigurationServer.GetService<ILocationService>();
        }

        public NetworkCredential GetCredentialsForTfsServer()
        {
            // the version of NetworkCredential inside of _tfsConfigurationServer may be non-null when _networkCredential is null b/c we may be using
            //      default credentials (aka string.IsNullOrEmpty(rawUserName)).  This then becomes the best way to get credentials.
            var tfsNetworkCredential = _tfsConfigurationServer.ClientCredentials.Windows.Credentials as NetworkCredential;
            bool credentialServerAuthenticationExists = tfsNetworkCredential != null && !string.IsNullOrEmpty(tfsNetworkCredential.UserName);
            if (credentialServerAuthenticationExists) return tfsNetworkCredential;
            return _networkCredential;
        }

        public NameValueCollection GetBasicAuthHeader()
        {
            var networkCredential = GetCredentialsForTfsServer();
            if (networkCredential == null) return null;
            var userName = networkCredential.UserName;
            var password = networkCredential.Password;
            string usernamePassword = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(string.Format("{0}:{1}", userName, password)));
            return new NameValueCollection
            {
                {"Authorization", "basic" + usernamePassword}
            };
        }
    }
}
