﻿using Newtonsoft.Json;
using NUnit.Framework;
using SirenOfShame.Lib.Watcher;
using SirenOfShame.Test.Unit.Resources;
using TfsRestServices;

namespace SirenOfShame.Test.Unit.CIEntryPointBuildStatus
{
    [TestFixture]
    public class TfsRestBuildStatusTest
    {
        [Test]
        public void GivenATraditionalXmlBuildDefinition_WhenWeParseIt_ThenWeParseItCorrectly()
        {
            var tfsRestWorkingBuild = ResourceManager.TfsRestBuildDefinitions1;
            var jsonWrapper = JsonConvert.DeserializeObject<TfsJsonWrapper<TfsJsonBuild>>(tfsRestWorkingBuild);
            var buildDefinition = jsonWrapper.Value[1];
            var buildStatus = new TfsRestBuildStatus(buildDefinition, new CommentsCache());
            Assert.AreEqual(BuildStatusEnum.Working, buildStatus.BuildStatusEnum);
            Assert.AreEqual("2", buildStatus.BuildDefinitionId);
            Assert.AreEqual("TestingGitTfsOnlineSolution", buildStatus.Name);
            Assert.AreEqual("Lee Richardson", buildStatus.RequestedBy);
            Assert.IsNull(buildStatus.Comment);
            Assert.AreEqual("https://sirenofshame.visualstudio.com/DefaultCollection/_permalink/_build/index?collectionId=3be0f19d-62d0-4f45-a140-f219cb9c08ae&projectId=cd1d630e-e0fc-46d3-9540-a477d17a84b1&buildId=18", buildStatus.Url);
            Assert.AreEqual("18", buildStatus.BuildId);
        }
   }
}
