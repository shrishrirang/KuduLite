using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Contracts.Settings;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Oryx;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Kudu.Core.SourceControl;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Collections.Generic;
using Kudu.Core.Helpers;
using System.Threading;
using Kudu.Contracts.Deployment;
using Kudu.Services.Arm;
using System.Text;

namespace Kudu.Services.Deployment
{
    public class PushDeploymentController : Controller
    {
        private const string DefaultDeployer = "Push-Deployer";
        private const string DefaultMessage = "Created via a push deployment";

        private readonly IEnvironment _environment;
        private readonly IFetchDeploymentManager _deploymentManager;
        private readonly ITracer _tracer;
        private readonly ITraceFactory _traceFactory;
        private readonly IDeploymentSettingsManager _settings;

        public PushDeploymentController(
            IEnvironment environment,
            IFetchDeploymentManager deploymentManager,
            ITracer tracer,
            ITraceFactory traceFactory,
            IDeploymentSettingsManager settings)
        {
            _environment = environment;
            _deploymentManager = deploymentManager;
            _tracer = tracer;
            _traceFactory = traceFactory;
            _settings = settings;
        }


        [HttpPost]
        [DisableRequestSizeLimit]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> ZipPushDeploy(
            [FromQuery] bool isAsync = false,
            [FromQuery] bool syncTriggers = false,
            [FromQuery] bool overwriteWebsiteRunFromPackage = false,
            [FromQuery] string author = null,
            [FromQuery] string authorEmail = null,
            [FromQuery] string deployer = DefaultDeployer,
            [FromQuery] string message = DefaultMessage)
        {
            using (_tracer.Step("ZipPushDeploy"))
            {
                var deploymentInfo = new ArtifactDeploymentInfo(_environment, _traceFactory)
                {
                    AllowDeploymentWhileScmDisabled = true,
                    Deployer = deployer,
                    IsContinuous = false,
                    AllowDeferredDeployment = false,
                    IsReusable = false,
                    TargetChangeset =
                        DeploymentManager.CreateTemporaryChangeSet(message: "Deploying from pushed zip file"),
                    CommitId = null,
                    RepositoryType = RepositoryType.None,
                    Fetch = LocalZipHandler,
                    DoFullBuildByDefault = false,
                    Author = author,
                    AuthorEmail = authorEmail,
                    Message = message,
                    RemoteURL = null,
                    DoSyncTriggers = syncTriggers,
                    OverwriteWebsiteRunFromPackage = overwriteWebsiteRunFromPackage && _environment.IsOnLinuxConsumption
                };

                if (_settings.RunFromLocalZip())
                {
                    // This is used if the deployment is Run-From-Zip
                    // the name of the deployed file in D:\home\data\SitePackages\{name}.zip is the 
                    // timestamp in the format yyyMMddHHmmss. 
                    deploymentInfo.ArtifactFileName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";
                    // This is also for Run-From-Zip where we need to extract the triggers
                    // for post deployment sync triggers.
                    deploymentInfo.SyncFunctionsTriggersPath =
                        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                };

                return await PushDeployAsync(deploymentInfo, isAsync, HttpContext);
            }
        }

        [HttpPut]
        public async Task<IActionResult> ZipPushDeployViaUrl(
            [FromBody] JObject requestJson,
            [FromQuery] bool isAsync = false,
            [FromQuery] bool syncTriggers = false,
            [FromQuery] bool overwriteWebsiteRunFromPackage = false,
            [FromQuery] string author = null,
            [FromQuery] string authorEmail = null,
            [FromQuery] string deployer = DefaultDeployer,
            [FromQuery] string message = DefaultMessage)
        {
            using (_tracer.Step("ZipPushDeployViaUrl"))
            {
                string zipUrl = GetArtifactURLFromJSON(requestJson);

                var deploymentInfo = new ArtifactDeploymentInfo(_environment, _traceFactory)
                {
                    AllowDeploymentWhileScmDisabled = true,
                    Deployer = deployer,
                    IsContinuous = false,
                    AllowDeferredDeployment = false,
                    IsReusable = false,
                    TargetChangeset =
                        DeploymentManager.CreateTemporaryChangeSet(message: "Deploying from pushed zip file"),
                    CommitId = null,
                    RepositoryType = RepositoryType.None,
                    Fetch = LocalZipHandler,
                    DoFullBuildByDefault = false,
                    Author = author,
                    AuthorEmail = authorEmail,
                    Message = message,
                    RemoteURL = zipUrl,
                    DoSyncTriggers = syncTriggers,
                    OverwriteWebsiteRunFromPackage = overwriteWebsiteRunFromPackage && _environment.IsOnLinuxConsumption
                };
                return await PushDeployAsync(deploymentInfo, isAsync, HttpContext);
            }
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> WarPushDeploy(
            [FromQuery] bool isAsync = false,
            [FromQuery] string author = null,
            [FromQuery] string authorEmail = null,
            [FromQuery] string deployer = DefaultDeployer,
            [FromQuery] string message = DefaultMessage)
        {
            using (_tracer.Step("WarPushDeploy"))
            {
                var appName = HttpContext.Request.Query["name"].ToString();
                if (string.IsNullOrWhiteSpace(appName))
                {
                    appName = "ROOT";
                }

                var deploymentInfo = new ArtifactDeploymentInfo(_environment, _traceFactory)
                {
                    AllowDeploymentWhileScmDisabled = true,
                    Deployer = deployer,
                    TargetDirectoryPath = Path.Combine("webapps", appName),
                    WatchedFilePath = Path.Combine("WEB-INF", "web.xml"),
                    IsContinuous = false,
                    AllowDeferredDeployment = false,
                    IsReusable = false,
                    CleanupTargetDirectory =
                        true, // For now, always cleanup the target directory. If needed, make it configurable
                    TargetChangeset =
                        DeploymentManager.CreateTemporaryChangeSet(message: "Deploying from pushed war file"),
                    CommitId = null,
                    RepositoryType = RepositoryType.None,
                    Fetch = LocalZipFetch,
                    DoFullBuildByDefault = false,
                    Author = author,
                    AuthorEmail = authorEmail,
                    Message = message,
                    RemoteURL = null
                };
                return await PushDeployAsync(deploymentInfo, isAsync, HttpContext);
            }
        }

        //
        // Supports:
        // 1. Deploy artifact in the request body:
        //    - For this: Query parameters should contain configuration.
        //                Example: /api/publish?type=war
        //                Request body should contain the artifact being deployed
        // 2. URL based deployment:
        //    - For this: Query parameters should contain configuration. Example: /api/publish?type=war
        //                Example: /api/publish?type=war
        //                Request body should contain JSON with 'packageUri' property pointing to the artifact location
        //                Example: { "packageUri": "http://foo/bar.war?accessToken=123" }
        // 3. ARM template based deployment:
        //    - For this: Query parameters are not supported.
        //                Request body should contain JSON with configuration as well as the artifact location
        //                Example: { "properties": { "type": "war", "packageUri": "http://foo/bar.war?accessToken=123" } }
        //
        [HttpPost]
        [HttpPut]
        [DisableRequestSizeLimit]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> OneDeploy(
            [FromQuery] string type = null,
            [FromQuery] bool async = false,
            [FromQuery] string path = null,
            [FromQuery] bool? restart = true,
            [FromQuery] bool? clean = null,
            [FromQuery] string stack = null,
            [FromQuery] bool ignoreStack = false
            )
        {
            string remoteArtifactUrl = null;

            using (_tracer.Step("OneDeploy"))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(Request.ContentType) && Request.ContentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                    {
                        string jsonString;
                        using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                        {
                            jsonString = await reader.ReadToEndAsync();
                        }

                        var requestJson = JObject.Parse(jsonString);

                        if (ArmUtils.IsArmRequest(Request)) // TODO(shrirs): Check if this works
                        {
                            requestJson = requestJson.Value<JObject>("properties");

                            type = requestJson.Value<string>("type");
                            async = requestJson.Value<bool>("async");
                            path = requestJson.Value<string>("path");
                            restart = requestJson.Value<bool?>("restart");
                            clean = requestJson.Value<bool?>("clean");
                            stack = requestJson.Value<string>("stack");
                            ignoreStack = requestJson.Value<bool>("ignorestack");
                        }

                        remoteArtifactUrl = GetArtifactURLFromJSON(requestJson);
                    }
                }
                catch (Exception ex)
                {
                    return StatusCode(StatusCodes.Status400BadRequest, ex);
                }

                //
                // 'async' is not a CSharp-ish variable name. And although it is a valid variable name, some
                // IDEs confuse it to be the 'async' keyword in C#.
                // On the other hand, isAsync is not a good name for the query-parameter.
                // So we use 'async' as the query parameter, and then assign it to the C# variable 'isAsync' 
                // at the earliest. Hereon, we use just 'isAsync'.
                // 
                bool isAsync = async;

                string websiteStack = !string.IsNullOrWhiteSpace(stack) ? stack : System.Environment.GetEnvironmentVariable(Constants.StackEnvVarName);

                ArtifactType artifactType = ArtifactType.Invalid;
                try
                {
                    artifactType = (ArtifactType)Enum.Parse(typeof(ArtifactType), type, ignoreCase: true);
                }
                catch
                {
                    return StatusCode(StatusCodes.Status400BadRequest, $"type='{type}' not recognized");
                }

                var deploymentInfo = new ArtifactDeploymentInfo(_environment, _traceFactory)
                {
                    ArtifactType = artifactType,
                    AllowDeploymentWhileScmDisabled = true,
                    Deployer = Constants.OneDeploy,
                    IsContinuous = false,
                    AllowDeferredDeployment = false,
                    IsReusable = false,
                    TargetRootPath = _environment.WebRootPath,
                    TargetChangeset = DeploymentManager.CreateTemporaryChangeSet(message: Constants.OneDeploy),
                    CommitId = null,
                    RepositoryType = RepositoryType.None,
                    RemoteURL = remoteArtifactUrl,
                    Fetch = OneDeployFetch,
                    DoFullBuildByDefault = false,
                    Message = Constants.OneDeploy,
                    WatchedFileEnabled = false,
                    RestartAllowed = restart.GetValueOrDefault(true),
                };

                switch (artifactType)
                {
                    case ArtifactType.War:
                        if (!ignoreStack && !string.Equals(websiteStack, Constants.Tomcat, StringComparison.OrdinalIgnoreCase))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest, $"WAR files cannot be deployed to stack='{websiteStack}'. Expected stack='{Constants.Tomcat}'");
                        }

                        // If path is non-null, we assume this is a legacy war deployment, i.e. equivalent of wardeploy
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            //
                            // For legacy war deployments, the only path allowed is webapps/<directory-name>
                            //

                            var segments = path.Split('/');
                            if (segments.Length != 2 || !path.StartsWith("webapps/") || string.IsNullOrWhiteSpace(segments[1]))
                            {
                                return StatusCode(StatusCodes.Status400BadRequest, $"path='{path}'. Only allowed path when type={artifactType} is webapps/<directory-name>. Example: path=webapps/ROOT");
                            }

                            deploymentInfo.TargetDirectoryPath = path;
                            deploymentInfo.Fetch = LocalZipHandler;
                            deploymentInfo.CleanupTargetDirectory = true;
                            artifactType = ArtifactType.Zip;
                        }
                        else
                        {
                            // For type=war, if no path is specified, the target file is app.war
                            deploymentInfo.TargetFileName = "app.war";
                        }

                        break;

                    case ArtifactType.Jar:
                        if (!ignoreStack && !string.Equals(websiteStack, Constants.JavaSE, StringComparison.OrdinalIgnoreCase))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest, $"JAR files cannot be deployed to stack='{websiteStack}'. Expected stack='{Constants.JavaSE}'");
                        }

                        deploymentInfo.TargetFileName = "app.jar";

                        break;

                    case ArtifactType.Ear:
                        // Currently not supported on Windows but here for future use
                        if (!ignoreStack && !string.Equals(websiteStack, Constants.JBossEap, StringComparison.OrdinalIgnoreCase))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest, $"EAR files cannot be deployed to stack='{websiteStack}'. Expected stack='{Constants.JBossEap}'");
                        }

                        deploymentInfo.TargetFileName = "app.ear";

                        break;

                    case ArtifactType.Lib:
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest, $"Path must be defined for library deployments");
                        }

                        deploymentInfo.TargetRootPath = Path.Combine(_environment.RootPath, Constants.LibsDirectoryRelativePath);
                        SetTargetDirectoyAndFileNameFromPath(deploymentInfo, path);

                        deploymentInfo.CleanupTargetDirectory = clean.GetValueOrDefault(false);

                        break;

                    case ArtifactType.Startup:
                        deploymentInfo.TargetRootPath = Path.Combine(_environment.RootPath, Constants.ScriptsDirectoryRelativePath);
                        SetTargetDirectoyAndFileNameFromPath(deploymentInfo, GetStartupFileName());

                        deploymentInfo.CleanupTargetDirectory = clean.GetValueOrDefault(false);

                        break;

                    case ArtifactType.Script:
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest, $"Path must be defined for script deployments");
                        }

                        deploymentInfo.TargetRootPath = Path.Combine(_environment.RootPath, Constants.ScriptsDirectoryRelativePath);
                        SetTargetDirectoyAndFileNameFromPath(deploymentInfo, path);

                        deploymentInfo.CleanupTargetDirectory = clean.GetValueOrDefault(false);

                        break;

                    case ArtifactType.Static:
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            return StatusCode(StatusCodes.Status400BadRequest, $"Path must be defined for static file deployments");
                        }

                        SetTargetDirectoyAndFileNameFromPath(deploymentInfo, path);

                        deploymentInfo.CleanupTargetDirectory = clean.GetValueOrDefault(false);

                        break;

                    case ArtifactType.Zip:
                        deploymentInfo.Fetch = LocalZipHandler;

                        deploymentInfo.TargetDirectoryPath = path;

                        deploymentInfo.CleanupTargetDirectory = clean.GetValueOrDefault(true);

                        break;

                    default:
                        return StatusCode(StatusCodes.Status400BadRequest, $"Artifact type '{artifactType}' not supported");
                }

                return await PushDeployAsync(deploymentInfo, isAsync, HttpContext, artifactType);
            }
        }

        private static void SetTargetDirectoyAndFileNameFromPath(DeploymentInfoBase deploymentInfo, string relativeFilePath)
        {
            // Extract directory path and file name from relativeFilePath
            // Example: path=a/b/c.jar => TargetDirectoryName=a/b and TargetFileName=c.jar
            // Example: path=c.jar => TargetDirectoryName=null and TargetFileName=c.jar
            // Example: path=/c.jar => TargetDirectoryName="" and TargetFileName=c.jar
            // Example: path=null => TargetDirectoryName=null and TargetFileName=null
            deploymentInfo.TargetFileName = Path.GetFileName(relativeFilePath);
            deploymentInfo.TargetDirectoryPath = Path.GetDirectoryName(relativeFilePath);
        }

        private static string GetStartupFileName()
        {
            return OSDetector.IsOnWindows() ? "startup.bat" : "startup.sh";
        }

        private string GetArtifactURLFromJSON(JObject requestObject)
        {
            using (_tracer.Step("Reading the zip URL from the request JSON"))
            {
                try
                {
                    string packageUri = requestObject.Value<string>("packageUri");
                    if (string.IsNullOrEmpty(packageUri))
                    {
                        throw new ArgumentException("Request body does not contain packageUri");
                    }

                    Uri zipUri = null;
                    if (!Uri.TryCreate(packageUri, UriKind.Absolute, out zipUri))
                    {
                        throw new ArgumentException("Malformed packageUri");
                    }
                    return packageUri;
                }
                catch (Exception ex)
                {
                    _tracer.TraceError(ex, "Error reading the URL from the JSON {0}", requestObject.ToString());
                    throw;
                }
            }
        }

        private async Task<IActionResult> PushDeployAsync(ArtifactDeploymentInfo deploymentInfo, bool isAsync,
            HttpContext context, ArtifactType artifactType = ArtifactType.Zip)
        {
            // TODO(shrirs): remove artifactType=zip param.

            string artifactTempPath;
            if (string.IsNullOrWhiteSpace(deploymentInfo.TargetFileName))
            {
                artifactTempPath = Path.Combine(_environment.ZipTempPath, Guid.NewGuid() + ".zip");
            }
            else
            {
                artifactTempPath = Path.Combine(_environment.ZipTempPath, deploymentInfo.TargetFileName);
            }

            if (_settings.RunFromLocalZip())
            {
                await WriteSitePackageZip(deploymentInfo, _tracer);
            }
            else
            {
                var oryxManifestFile = Path.Combine(_environment.WebRootPath, "oryx-manifest.toml");
                if (FileSystemHelpers.FileExists(oryxManifestFile))
                {
                    _tracer.Step("Removing previous build artifact's manifest file");
                    FileSystemHelpers.DeleteFileSafe(oryxManifestFile);
                }

                try
                {
                    var nodeModulesSymlinkFile = Path.Combine(_environment.WebRootPath, "node_modules");
                    Mono.Unix.UnixSymbolicLinkInfo i = new Mono.Unix.UnixSymbolicLinkInfo(nodeModulesSymlinkFile);
                    if (i.FileType == Mono.Unix.FileTypes.SymbolicLink)
                    {
                        _tracer.Step("Removing node_modules symlink");
                        // TODO: Add support to remove Unix Symlink File in DeleteFileSafe
                        // FileSystemHelpers.DeleteFileSafe(nodeModulesSymlinkFile); 
                        FileSystemHelpers.RemoveUnixSymlink(nodeModulesSymlinkFile, TimeSpan.FromSeconds(5));
                    }
                }
                catch(Exception)
                {
                    // best effort
                }

                using (_tracer.Step("Writing artifact to {0}", artifactTempPath))
                {
                    if (!string.IsNullOrEmpty(context.Request.ContentType) &&
                        context.Request.ContentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                    {
                        FormValueProvider formModel;
                        using (_tracer.Step("Writing zip file to {0}", artifactTempPath))
                        {
                            using (var file = System.IO.File.Create(artifactTempPath))
                            {
                                formModel = await Request.StreamFile(file);
                            }
                        }
                    }
                    else if (deploymentInfo.RemoteURL != null)
                    {
                        using (_tracer.Step("Writing zip file from packageUri to {0}", artifactTempPath))
                        {
                            using (var httpClient = new HttpClient())
                            using (var fileStream = new FileStream(artifactTempPath,
                                FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                            {
                                var zipUrlRequest = new HttpRequestMessage(HttpMethod.Get, deploymentInfo.RemoteURL);
                                var zipUrlResponse = await httpClient.SendAsync(zipUrlRequest);

                                try
                                {
                                    zipUrlResponse.EnsureSuccessStatusCode();
                                }
                                catch (HttpRequestException hre)
                                {
                                    _tracer.TraceError(hre, "Failed to get file from packageUri {0}", deploymentInfo.RemoteURL);
                                    throw;
                                }

                                using (var content = await zipUrlResponse.Content.ReadAsStreamAsync())
                                {
                                    await content.CopyToAsync(fileStream);
                                }
                            }
                        }
                    }
                    else
                    {
                        using (var file = System.IO.File.Create(artifactTempPath))
                        {
                            await Request.Body.CopyToAsync(file);
                        }
                    }

                    deploymentInfo.RepositoryUrl = artifactTempPath;
                }
            }

            var result =
                await _deploymentManager.FetchDeploy(deploymentInfo, isAsync, UriHelper.GetRequestUri(Request), "HEAD");

            switch (result)
            {
                case FetchDeploymentRequestResult.RunningAynschronously:
                    if (isAsync)
                    {
                        // latest deployment keyword reserved to poll till deployment done
                        Response.GetTypedHeaders().Location =
                            new Uri(UriHelper.GetRequestUri(Request),
                                String.Format("/api/deployments/{0}?deployer={1}&time={2}", Constants.LatestDeployment,
                                    deploymentInfo.Deployer, DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ")));
                    }

                    return Accepted();
                case FetchDeploymentRequestResult.ForbiddenScmDisabled:
                    // Should never hit this for zip push deploy
                    _tracer.Trace("Scm is not enabled, reject all requests.");
                    return Forbid();
                case FetchDeploymentRequestResult.ConflictAutoSwapOngoing:
                    return StatusCode(StatusCodes.Status409Conflict, Resources.Error_AutoSwapDeploymentOngoing);
                case FetchDeploymentRequestResult.Pending:
                    // Shouldn't happen here, as we disallow deferral for this use case
                    return Accepted();
                case FetchDeploymentRequestResult.RanSynchronously:
                    return Ok();
                case FetchDeploymentRequestResult.ConflictDeploymentInProgress:
                    return StatusCode(StatusCodes.Status409Conflict, Resources.Error_DeploymentInProgress);
                case FetchDeploymentRequestResult.ConflictRunFromRemoteZipConfigured:
                    return StatusCode(StatusCodes.Status409Conflict, Resources.Error_RunFromRemoteZipConfigured);
                default:
                    return BadRequest();
            }
        }


        private Task LocalZipFetch(IRepository repository, DeploymentInfoBase deploymentInfo, string targetBranch,
            ILogger logger, ITracer tracer)
        {
            var zipDeploymentInfo = (ArtifactDeploymentInfo)deploymentInfo;

            // For this kind of deployment, RepositoryUrl is a local path.
            var sourceZipFile = zipDeploymentInfo.RepositoryUrl;
            var extractTargetDirectory = repository.RepositoryPath;

            var info = FileSystemHelpers.FileInfoFromFileName(sourceZipFile);
            var sizeInMb = (info.Length / (1024f * 1024f)).ToString("0.00", CultureInfo.InvariantCulture);

            var message = String.Format(
                CultureInfo.InvariantCulture,
                "Cleaning up temp folders from previous zip deployments and extracting pushed zip file {0} ({1} MB) to {2}",
                info.FullName,
                sizeInMb,
                extractTargetDirectory);

            using (tracer.Step(message))
            {
                // If extractTargetDirectory already exists, rename it so we can delete it concurrently with
                // the unzip (along with any other junk in the folder)
                var targetInfo = FileSystemHelpers.DirectoryInfoFromDirectoryName(extractTargetDirectory);
                if (targetInfo.Exists)
                {
                    var moveTarget = Path.Combine(targetInfo.Parent.FullName, Path.GetRandomFileName());
                    targetInfo.MoveTo(moveTarget);
                }

                DeleteFilesAndDirsExcept(sourceZipFile, extractTargetDirectory, tracer);

                //
                // We want to create a directory hierarchy under 'extractTargetDirectory'
                // such that it exactly matches deploymentInfo.TargetDirectoryPath
                // We do this only for OneDeploy scenarios, but when OneDeploy is
                // deploying a war, we don't want to do that as that is equivalent to doing
                // wardeploy.
                // 
                string extractSubDirectoryPath = extractTargetDirectory;

                if (!string.IsNullOrWhiteSpace(deploymentInfo.TargetDirectoryPath) &&
                    deploymentInfo.Deployer == Constants.OneDeploy &&
                    deploymentInfo.ArtifactType != ArtifactType.War)
                {
                    extractSubDirectoryPath = Path.Combine(extractTargetDirectory, deploymentInfo.TargetDirectoryPath);
                }

                FileSystemHelpers.CreateDirectory(extractSubDirectoryPath);

                using (var file = info.OpenRead())

                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    deploymentInfo.repositorySymlinks = zip.Extract(extractSubDirectoryPath, preserveSymlinks: ShouldPreserveSymlinks());

                    CreateZipSymlinks(deploymentInfo.repositorySymlinks, extractSubDirectoryPath);

                    PermissionHelper.ChmodRecursive("777", extractSubDirectoryPath, tracer, TimeSpan.FromMinutes(1));
                }
            }

            CommitRepo(repository, zipDeploymentInfo);
            return Task.CompletedTask;
        }

        private async Task OneDeployFetch(IRepository repository, DeploymentInfoBase deploymentInfo, string targetBranch,
            ILogger logger, ITracer tracer)
        {
            var artifactDeploymentInfo = (ArtifactDeploymentInfo)deploymentInfo;

            // For this kind of deployment, RepositoryUrl is a local path.
            var sourceZipFile = artifactDeploymentInfo.RepositoryUrl;

            // This is the path where the artifact being deployed is staged, before it is copied to the final target location
            var artifactDirectoryStagingPath = repository.RepositoryPath;

            var info = FileSystemHelpers.FileInfoFromFileName(sourceZipFile);
            var sizeInMb = (info.Length / (1024f * 1024f)).ToString("0.00", CultureInfo.InvariantCulture);

            var message = String.Format(
                CultureInfo.InvariantCulture,
                "Cleaning up temp folders from previous zip deployments and extracting pushed zip file {0} ({1} MB) to {2}",
                info.FullName,
                sizeInMb,
                artifactDirectoryStagingPath);

            using (tracer.Step(message))
            {
                var targetInfo = FileSystemHelpers.DirectoryInfoFromDirectoryName(artifactDirectoryStagingPath);
                if (targetInfo.Exists)
                {
                    // If the staging path already exists, rename it so we can delete it later 
                    var moveTarget = Path.Combine(targetInfo.Parent.FullName, Path.GetRandomFileName());
                    using (tracer.Step(string.Format("Renaming ({0}) to ({1})", targetInfo.FullName, moveTarget)))
                    {
                        targetInfo.MoveTo(moveTarget);
                    }
                }

                //
                // We want to create the directory hierarchy under artifactDirectoryStagingPath
                // that exactly matches artifactDeploymentInfo.TargetDirectoryPath
                //
                string stagingSubDirPath = artifactDirectoryStagingPath;

                if (!string.IsNullOrWhiteSpace(artifactDeploymentInfo.TargetDirectoryPath))
                {
                    stagingSubDirPath = Path.Combine(artifactDirectoryStagingPath, artifactDeploymentInfo.TargetDirectoryPath);
                }

                // Create artifact staging directory hierarchy before later use 
                Directory.CreateDirectory(stagingSubDirPath);

                var artifactFileStagingPath = Path.Combine(stagingSubDirPath, deploymentInfo.TargetFileName);

                // TODO(shrirs): remove this if not needed
                // If RemoteUrl is non-null, it means the content needs to be downloaded from the Url source to the staging location
                // Else, it had been downloaded already so we just move the downloaded file to the staging location
                //if (!string.IsNullOrWhiteSpace(artifactDeploymentInfo.RemoteURL))
                //{
                //    using (tracer.Step("Saving request content to {0}", artifactFileStagingPath))
                //    {
                //        // TODO(shrirs) var content = await DeploymentHelper.GetArtifactContentFromURL(artifactDeploymentInfo, tracer);
                //        var copyTask = Task.CompletedTask; // TODO(shrirs) content.CopyToAsync(artifactFileStagingPath, tracer);

                //        // Deletes all files and directories except for artifactFileStagingPath and artifactDirectoryStagingPath
                //        var cleanTask = Task.Run(() => DeleteFilesAndDirsExcept(artifactFileStagingPath, artifactDirectoryStagingPath, tracer));

                //        // Lets the copy and cleanup tasks to run in parallel and wait for them to finish 
                //        await Task.WhenAll(copyTask, cleanTask);
                //    }
                //}
                //else
                {
                    var srcInfo = FileSystemHelpers.DirectoryInfoFromDirectoryName(deploymentInfo.RepositoryUrl);
                    using (tracer.Step(string.Format("Moving {0} to {1}", targetInfo.FullName, artifactFileStagingPath)))
                    {
                        srcInfo.MoveTo(artifactFileStagingPath);
                    }

                    // Deletes all files and directories except for artifactFileStagingPath and artifactDirectoryStagingPath
                    DeleteFilesAndDirsExcept(artifactFileStagingPath, artifactDirectoryStagingPath, tracer);
                }

                // The deployment flow expects at least 1 commit in the IRepository commit, refer to CommitRepo() for more info
                CommitRepo(repository, artifactDeploymentInfo);
            }
        }

        private async Task LocalZipHandler(IRepository repository, DeploymentInfoBase deploymentInfo,
            string targetBranch, ILogger logger, ITracer tracer)
        {
            if (_settings.RunFromLocalZip() && deploymentInfo is ArtifactDeploymentInfo)
            {
                // If this is a Run-From-Zip deployment, then we need to extract function.json
                // from the zip file into path zipDeploymentInfo.SyncFunctionsTrigersPath
                ExtractTriggers(repository, deploymentInfo as ArtifactDeploymentInfo);
            }
            else
            {
                await LocalZipFetch(repository, deploymentInfo, targetBranch, logger, tracer);
            }
        }

        private void ExtractTriggers(IRepository repository, ArtifactDeploymentInfo zipDeploymentInfo)
        {
            FileSystemHelpers.EnsureDirectory(zipDeploymentInfo.SyncFunctionsTriggersPath);
            // Loading the zip file depends on how fast the file system is.
            // Tested Azure Files share with a zip containing 120k files (160 MBs)
            // takes 20 seconds to load. On my machine it takes 900 msec.
            using (var zip = ZipFile.OpenRead(Path.Combine(_environment.SitePackagesPath, zipDeploymentInfo.ArtifactFileName)))
            {
                var entries = zip.Entries
                    // Only select host.json, proxies.json, or function.json that are from top level directories only
                    // Tested with a zip containing 120k files, and this took 90 msec
                    // on my machine.
                    .Where(e =>
                        e.FullName.Equals(Constants.FunctionsHostConfigFile, StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.Equals(Constants.ProxyConfigFile, StringComparison.OrdinalIgnoreCase) ||
                        isFunctionJson(e.FullName));

                foreach (var entry in entries)
                {
                    var path = Path.Combine(zipDeploymentInfo.SyncFunctionsTriggersPath, entry.FullName);
                    FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path, overwrite: true);
                }
            }

            CommitRepo(repository, zipDeploymentInfo);

            bool isFunctionJson(string fullName)
            {
                return fullName.EndsWith(Constants.FunctionsConfigFile) &&
                       fullName.Count(c => c == '/' || c == '\\') == 1;
            }
        }

        private static void CommitRepo(IRepository repository, ArtifactDeploymentInfo zipDeploymentInfo)
        {
            // Needed in order for repository.GetChangeSet() to work.
            // Similar to what OneDriveHelper and DropBoxHelper do.
            // We need to make to call respository.Commit() since deployment flow expects at
            // least 1 commit in the IRepository. Even though there is no repo per se in this
            // scenario, deployment pipeline still generates a NullRepository
            repository.Commit(zipDeploymentInfo.Message, zipDeploymentInfo.Author, zipDeploymentInfo.AuthorEmail);
            Thread.Sleep(2000);
        }

        private static void CreateZipSymlinks(IDictionary<string, string> symLinks, string extractTargetDirectory)
        {
            if (!OSDetector.IsOnWindows() && symLinks != null)
            {
                foreach (var symlinkPair in symLinks)
                {
                    string symLinkFilePath = Path.Combine(extractTargetDirectory, symlinkPair.Key);
                    FileSystemHelpers.EnsureDirectory(FileSystemHelpers.GetDirectoryName(Path.Combine(extractTargetDirectory, symlinkPair.Key)));
                    FileSystemHelpers.CreateRelativeSymlink(symLinkFilePath, symlinkPair.Value, TimeSpan.FromSeconds(5));
                }
            }
        }

        private async Task WriteSitePackageZip(ArtifactDeploymentInfo zipDeploymentInfo, ITracer tracer)
        {
            var filePath = Path.Combine(_environment.SitePackagesPath, zipDeploymentInfo.ArtifactFileName);
            // Make sure D:\home\data\SitePackages exists
            FileSystemHelpers.EnsureDirectory(_environment.SitePackagesPath);
            using (_tracer.Step("Writing zip file to {0}", filePath))
            {
                if (HttpContext.Request.ContentType.Contains("multipart/form-data",
                    StringComparison.OrdinalIgnoreCase))
                {
                    FormValueProvider formModel;
                    using (_tracer.Step("Writing zip file to {0}", filePath))
                    {
                        using (var file = System.IO.File.Create(filePath))
                        {
                            formModel = await Request.StreamFile(file);
                        }
                    }
                }
                else
                {
                    using (var file = System.IO.File.Create(filePath))
                    {
                        await Request.Body.CopyToAsync(file);
                    }
                }
            }

            DeploymentHelper.PurgeBuildArtifactsIfNecessary(_environment.SitePackagesPath, BuildArtifactType.Zip,
                tracer, _settings.GetMaxZipPackageCount());
        }

        private static bool ShouldPreserveSymlinks()
        {
            string framework = System.Environment.GetEnvironmentVariable("FRAMEWORK");
            string preserveSymlinks = System.Environment.GetEnvironmentVariable("WEBSITE_ZIP_PRESERVE_SYMLINKS");
            return !string.IsNullOrEmpty(framework) 
                && framework.Equals("node", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(preserveSymlinks)
                && preserveSymlinks.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private void DeleteFilesAndDirsExcept(string fileToKeep, string dirToKeep, ITracer tracer)
        {
            // Best effort. Using the "Safe" variants does retries and swallows exceptions but
            // we may catch something non-obvious.
            try
            {
                var files = FileSystemHelpers.GetFiles(_environment.ZipTempPath, "*")
                    .Where(p => !PathUtilityFactory.Instance.PathsEquals(p, fileToKeep));

                foreach (var file in files)
                {
                    FileSystemHelpers.DeleteFileSafe(file);
                }

                var dirs = FileSystemHelpers.GetDirectories(_environment.ZipTempPath)
                    .Where(p => !PathUtilityFactory.Instance.PathsEquals(p, dirToKeep));

                foreach (var dir in dirs)
                {
                    FileSystemHelpers.DeleteDirectorySafe(dir);
                }
            }
            catch (Exception ex)
            {
                tracer.TraceError(ex, "Exception encountered during zip folder cleanup");
                throw;
            }
        }
    }
}