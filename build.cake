#tool nuget:?package=Cake.Powershell
#tool nuget:?package=NUnit.ConsoleRunner
#tool nuget:?package=Pickles
#tool nuget:?package=System.Threading
#tool nuget:?package=Cake.Http

#addin "Cake.Powershell"
#addin "Cake.FileHelpers"
#addin "System.Threading"
#addin "Cake.Slack"
#addin "System.IO"
#addin "Cake.Http"
//#r "./test/nunit.framework.dll"
//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Debug");
var branchName = Argument("branchName", "develop");
var cluster = Argument("Cluster", "PoC");

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Define directories.
var buildDir = Directory("./src/Example/bin") + Directory(configuration);
var publishProfileDir = Directory("./MultiBannerBackend/PublishProfiles/Cloud.xml");
var packageDir = Directory("./MultiBannerBackend/pkg/Debug");
var jiraSpecDir = Directory("./Backend.Test.Spec/Backend.Test.Spec.csproj");
var testDir = Directory("./test");
var testDirPath = File("./test/File.dll");


//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(packageDir);
	CleanDirectory(testDir);	
});

Task("Clean-Sdk")
    .Does(() =>
{
	CleanDirectory("./sdk");
});

Task("Restore-NuGet-Packages")

    .IsDependentOn("Clean")
    .Does(() =>
{
    NuGetRestore("./MultiBannerBackend.sln");
});


Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    if(IsRunningOnWindows())
    {
      // Use MSBuild
		MSBuild("./MultiBannerBackend/MultiBannerBackend.sfproj", configurator =>
		configurator.SetConfiguration(configuration)
        .SetVerbosity(Verbosity.Normal)
        .UseToolVersion(MSBuildToolVersion.VS2015)
        .SetMSBuildPlatform(MSBuildPlatform.x64)
        .SetPlatformTarget(PlatformTarget.x64)		
		.WithTarget("Package"));
    }
    else
    {
      // Use XBuild
      XBuild("./MultiBannerBackend.sln", settings =>
        settings.SetConfiguration(configuration));
    }
});

Task("Deploy-Announce-Start")
    .Does(() =>
{
	var slackToken = "xoxp-79680605136-90492171008-154313915862-e1056fa3771cd7eceb994f2857186ccc";
	var slackChannel = "#general";
	if(cluster == "Stage") {
		var postMessageResult = Slack.Chat.PostMessage(
					token:slackToken,
					channel:slackChannel,
					text:"Initiating API Services Build on " + cluster + "."
		);
		if (postMessageResult.Ok){
			Information("Message {0} successfully sent", postMessageResult.TimeStamp);
		}
		else{
			Error("Failed to send message: {0}", postMessageResult.Error);
		}
	}
});

Task("Deploy-Application")
    .Does(() =>
{
	StartPowershellScript(". ./MultiBannerBackend/Scripts/Deploy-FabricApplication.ps1", new PowershellSettings()
		.WithArguments(args => 
        {
            args.AppendQuoted("PublishProfileFile", "./MultiBannerBackend/PublishProfiles/"+cluster+".xml");
			args.Append("ApplicationPackagePath", packageDir);
			args.Append("UnregisterUnusedApplicationVersionsAfterUpgrade", "$False");
			args.AppendQuoted("OverrideUpgradeBehavior", "None");
			args.AppendQuoted("OverwriteBehavior", "Always");
			args.Append("-Verbose");
	
	    }));
});

Task("Deploy-Wait-For-Completion")
    .Does(() =>
{
// we try several times as the script gives up too easily
	for(var i = 0; i <= 10; i++)
	{
	StartPowershellScript(". ./MultiBannerBackend/Scripts/Validate-FabricApplication.ps1", new PowershellSettings()
		.WithArguments(args => 
        {
		    args.AppendQuoted("PublishProfileFile", "./MultiBannerBackend/PublishProfiles/"+cluster+".xml");
	    }));
    }
//wait one more minute for all of the services to be up
	System.Threading.Thread.Sleep(60000);

});

Task("Deploy-Announce-Stop")
    .Does(() =>
{
	var slackToken = "xoxp-79680605136-90492171008-154313915862-e1056fa3771cd7eceb994f2857186ccc";
	var slackChannel = "#general";
	if(cluster == "Stage") {
		var preMessageResult = Slack.Chat.PostMessage(
					token:slackToken,
					channel:slackChannel,
					text:"Completed API Services Build on " + cluster + "."
			);
		if (preMessageResult.Ok){
			Information("Message {0} successfully sent", preMessageResult.TimeStamp);
		}
		else{
			Error("Failed to send message: {0}", preMessageResult.Error);
		}
	}
	
});

Task("Build-Jira")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    if(IsRunningOnWindows())
    {
		MSBuild("./Backend.Test.Spec/Backend.Test.Spec.csproj", configurator =>
		configurator.SetConfiguration(configuration)
        .SetVerbosity(Verbosity.Normal)
        .UseToolVersion(MSBuildToolVersion.VS2015)
        .SetMSBuildPlatform(MSBuildPlatform.x64)
        .SetPlatformTarget(PlatformTarget.x64)
		.WithProperty("OutputPath", "../test"));
    }
    else
    {
      // Use XBuild
      XBuild("./Backend.Test.Spec/Backend.Test.Spec.csproj", settings =>
        settings.SetConfiguration(configuration));
    }
});


Task("Run-Spec-Tests")
    .IsDependentOn("Build-Jira")
	.ContinueOnError()
    .Does(() =>
{
    var testAssemblies = GetFiles("./test/Backend.Test.Spec.dll");		
    var outFile = File("./test/Backend.Test.Spec.dll.out");		
    var xmlFile = File("./test/Backend.Test.Spec.dll.xml");		
	NUnit3(testAssemblies,
		new NUnit3Settings {
		//ArgumentCustomization = args=>args.Append("-parallel none"),
		//Agents = 1,
		//Workers = 1,
		//Parallelism = ParallelismOption.None,
		// OutputFile = outFile,
		Results = xmlFile,
		//MaxThreads = 1,
		//ShadowCopy = false,
		ToolTimeout = TimeSpan.FromMinutes(60)
	});
})
.ReportError(exception =>
{  
	// Report the error.
});

Task("Generate-JUnit")
	.IsDependentOn("Run-Spec-Tests")
	.Does(() =>
{
	StartPowershellScript(". .\\make-junit.ps1", new PowershellSettings());
});

Task("Pickle-Reports")
	.Does(() =>
{
	FilePath initPath = Context.Tools.Resolve("PicklesDoc.Pickles.PowerShell.dll");
	StartPowershellScript(". Import-Module (Resolve-Path " + initPath.ToString()+"); Pickle-Features", new PowershellSettings()
		.WithArguments(args => 
        {
			args.Append("FeatureDirectory", ".\\Backend.Test.Spec\\Features");
			args.Append("OutputDirectory", ".\\test");
			args.Append("SystemUnderTestName", "Multibanner-API");
			args.Append("SystemUnderTestVersion", "V5");
			args.Append("TestResultsFile", ".\\test\\Backend.Test.Spec.dll.xml");
			args.Append("DocumentationFormat", "Cucumber");
	    }));

});

Task("Generate-Sdk")
	.IsDependentOn("Clean-Sdk")
	//.IsDependentOn("Build")
	//.IsDependentOn("Deploy")
    .Does(() =>
{
	System.Threading.Thread.Sleep(60000);

	CreateDirectory(Directory("./Sdk/SwaggerClient"));
	Unzip("./Backend.Common/Deployment/CI/Multibanner.API.ProxyClient-Master.zip", Directory("./Sdk"));
	StartPowershellScript("java", args =>
        {
            args.Append("jar", File("./Backend.Common/Deployment/CI/swagger-codegen-cli-2.2.1.jar"));
			args.Append("generate");
			args.Append("i", "http://stg-thegoods.eastus.cloudapp.azure.com/swagger/v5/");
			args.Append("l", "csharp");
			args.Append("c", File("./Backend.Common/Deployment/CI/config.json"));
			args.Append("o", Directory("./sdk/SwaggerClient"));
        });
	
	CopyDirectory("./sdk/Master", "./sdk/SwaggerClient");
	CopyDirectory("./sdk/SwaggerClient/src/Multibanner.API.ProxyClient/Api", "./sdk/SwaggerClient/src/Multibanner.API.ProxyClient.Common/Api");
	CopyDirectory("./sdk/SwaggerClient/src/Multibanner.API.ProxyClient/Model", "./sdk/SwaggerClient/src/Multibanner.API.ProxyClient.Common/Model");
	CopyDirectory("./sdk/SwaggerClient/src/Multibanner.API.ProxyClient/Client", "./sdk/SwaggerClient/src/Multibanner.API.ProxyClient.Common/Client");

	var updatedText = FileReadText("./sdk/SwaggerClient/src/Multibanner.API.ProxyClient.Common/Client/ApiClient.cs");
	updatedText = updatedText.Replace("using System.Web;",string.Empty);
	updatedText = updatedText.Replace("dynamic", "object");
	FileWriteText("./sdk/SwaggerClient/src/Multibanner.API.ProxyClient.Common/Client/ApiClient.cs", updatedText);
	
	NuGetRestore("./sdk/SwaggerClient/Multibanner.API.ProxyClient.sln");
	MSBuild("./sdk/SwaggerClient/Multibanner.API.ProxyClient.sln", configurator =>
		configurator.SetConfiguration(configuration)
        .WithProperty("AndroidSdkDirectory", "C:\\android-sdk")
        .SetVerbosity(Verbosity.Normal)
		.WithProperty("AndroidSdkDirectory", "C:\\android-sdk")
        .UseToolVersion(MSBuildToolVersion.NET45)
        .UseToolVersion(MSBuildToolVersion.VS2015));

});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Run-Specs");

Task("Deploy")
    .IsDependentOn("Deploy-Announce-Start")
    .IsDependentOn("Deploy-Application")
    .IsDependentOn("Deploy-Wait-For-Completion")
    .IsDependentOn("Deploy-Announce-Stop");


Task("Run-Specs")
    .IsDependentOn("Generate-JUnit");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
