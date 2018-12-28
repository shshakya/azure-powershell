﻿
// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------


using Microsoft.Azure.Commands.WebApps.Models;
using Microsoft.Azure.Management.WebSites.Models;
using System;
using System.IO;
using System.Management.Automation;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace Microsoft.Azure.Commands.WebApps.Cmdlets.WebApps
{
    /// <summary>
    /// Deploy a web app from a ZIP, WAR, or JAR archive.
    /// </summary>
    [Cmdlet("Publish", ResourceManager.Common.AzureRMConstants.AzureRMPrefix + "WebApp"), OutputType(typeof(PSSite))]
    [OutputType(typeof(string))]
    public class PublishAzureWebAppCmdlet : WebAppOptionalSlotBaseCmdlet
    {
        // Poll status for a maximum of 20 minutes (1200 seconds / 2 seconds per status check)
        private const int NumStatusChecks = 600;

        [Parameter(Mandatory = true, HelpMessage = "The path of the archive file. ZIP, WAR, and JAR are supported.")]
        [ValidateNotNullOrEmpty]
        public string ArchivePath { get; set; }

        [Parameter(Mandatory = false, HelpMessage = "Run cmdlet in the background")]
        public SwitchParameter AsJob { get; set; }

        public override void ExecuteCmdlet()
        {
            base.ExecuteCmdlet();
            User user = WebsitesClient.GetPublishingCredentials(ResourceGroupName, Name, Slot);

            HttpResponseMessage r;
            string deployUrl;
            string deploymentStatusUrl = user.ScmUri + "/api/deployments/latest";

            if (ArchivePath.ToLower().EndsWith("war"))
            {
                deployUrl = user.ScmUri + "/api/wardeploy?isAsync=true";
            }
            else if (ArchivePath.ToLower().EndsWith("zip") || ArchivePath.ToLower().EndsWith("jar"))
            {
                deployUrl = user.ScmUri + "/api/zipdeploy?isAsync=true";
            }
            else
            {
                throw new Exception("Unknown archive type.");
            }

            using (var s = File.OpenRead(ArchivePath))
            {
                HttpClient client = new HttpClient();
                var byteArray = Encoding.ASCII.GetBytes(user.PublishingUserName + ":" + user.PublishingPassword);
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                HttpContent fileContent = new StreamContent(s);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/form-data");
                r = client.PostAsync(deployUrl, fileContent).Result;

                int numChecks = 0;
                do
                {
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                    r = client.GetAsync(deploymentStatusUrl).Result;
                    numChecks++;
                } while (r.StatusCode == HttpStatusCode.Accepted && numChecks < NumStatusChecks);

                if (r.StatusCode == HttpStatusCode.Accepted && numChecks >= NumStatusChecks)
                {
                    WriteWarning("Maximum status polling time exceeded. Deployment is still in progress.");
                }
                else if (r.StatusCode != HttpStatusCode.OK)
                {
                    WriteWarning("Deployment failed with status code=" + r.StatusCode + " reason=" + r.ReasonPhrase);
                }
            }

            PSSite app = new PSSite(WebsitesClient.GetWebApp(ResourceGroupName, Name, Slot));
            WriteObject(app);
        }

    }
}
